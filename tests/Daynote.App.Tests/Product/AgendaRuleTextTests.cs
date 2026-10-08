using Daynote.App.Localization;
using Daynote.App.Notes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Product;

/// <summary>
/// What a recurrence rule is called, and the notice for one the app cannot schedule
/// (docs/design-renewal/Daynote Mobile B Tasks - Events 06 Alerts).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class AgendaRuleTextTests
{
    private AppLanguage original;

    [TestInitialize]
    public void Setup() => original = LocalizationService.Instance.Language;

    [TestCleanup]
    public void Restore() => LocalizationService.Instance.SetLanguage(original);

    [TestMethod]
    public void A_rule_the_app_schedules_is_named_the_short_way()
    {
        LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
        Assert.AreEqual("매일", AgendaRuleText.Describe("FREQ=DAILY"));
        Assert.AreEqual("매주 월", AgendaRuleText.Describe("FREQ=WEEKLY;BYDAY=MO"));
        Assert.AreEqual("2주마다 월·금", AgendaRuleText.Describe("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,FR"));

        LocalizationService.Instance.SetLanguage(AppLanguage.English);
        Assert.AreEqual("Daily", AgendaRuleText.Describe("FREQ=DAILY"));
        Assert.AreEqual("Weekly on Mon", AgendaRuleText.Describe("FREQ=WEEKLY;BYDAY=MO"));
        Assert.AreEqual("Every 3 days", AgendaRuleText.Describe("FREQ=DAILY;INTERVAL=3"));
    }

    [TestMethod]
    public void A_rule_the_app_cannot_schedule_is_still_named()
    {
        // The whole reason naming and scheduling are separate jobs. The notice puts the name in
        // its sentence, so "this repeat" has to be something the user can see.
        LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
        Assert.AreEqual("매월 25일", AgendaRuleText.Describe("FREQ=MONTHLY;BYMONTHDAY=25"));
        Assert.AreEqual("매월 둘째 주 월요일", AgendaRuleText.Describe("FREQ=MONTHLY;BYDAY=2MO"));
        Assert.AreEqual("매년", AgendaRuleText.Describe("FREQ=YEARLY"));

        LocalizationService.Instance.SetLanguage(AppLanguage.English);
        Assert.AreEqual("Monthly on the 25th", AgendaRuleText.Describe("FREQ=MONTHLY;BYMONTHDAY=25"));
        Assert.AreEqual("Monthly on the second Mon", AgendaRuleText.Describe("FREQ=MONTHLY;BYDAY=2MO"));
    }

    [TestMethod]
    public void An_rrule_with_no_frequency_it_knows_gets_a_vague_true_word()
    {
        // Better than a precise false one, and better than an empty label on a row that plainly
        // repeats.
        LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
        Assert.AreEqual("반복", AgendaRuleText.Describe("FREQ=HOURLY"));
        Assert.AreEqual(string.Empty, AgendaRuleText.Describe(null));
    }

    [TestMethod]
    public void The_notice_appears_only_for_a_rule_the_app_cannot_schedule()
    {
        LocalizationService.Instance.SetLanguage(AppLanguage.Korean);

        // Daily and weekly — intervals, named days, a count, an end date — alert like any other
        // to-do, so there is nothing to warn about.
        Assert.AreEqual(string.Empty, AgendaRuleText.AlertsUnsupportedNotice("FREQ=DAILY"));
        Assert.AreEqual(
            string.Empty,
            AgendaRuleText.AlertsUnsupportedNotice("FREQ=WEEKLY;BYDAY=MO;INTERVAL=2;COUNT=5"));
        Assert.AreEqual(string.Empty, AgendaRuleText.AlertsUnsupportedNotice(null));

        Assert.AreEqual(
            "이 반복(매월 25일)은 아직 알림을 보내지 않아요. 매일·매주 반복은 알림이 옵니다.",
            AgendaRuleText.AlertsUnsupportedNotice("FREQ=MONTHLY;BYMONTHDAY=25"));

        LocalizationService.Instance.SetLanguage(AppLanguage.English);
        Assert.AreEqual(
            "Alerts aren’t available for this repeat (Monthly on the 25th) yet. "
                + "Daily and weekly repeats send alerts.",
            AgendaRuleText.AlertsUnsupportedNotice("FREQ=MONTHLY;BYMONTHDAY=25"));
    }
}
