using Daynote.App.Localization;
using Daynote.App.Notes;
using Daynote.Core.Agenda;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Product;

/// <summary>
/// The sentences the <c>@</c> popup reads back (docs/TODOS.md §7).
/// </summary>
/// <remarks>
/// Every expected string below is copied from a readback line in the design mocks — the desktop's
/// §01 and the phone's §01 — at their shared reference moment of Wednesday 7 October 2026, 14:30.
/// They are written out in full rather than composed, because composing them in the test would
/// repeat the bug the format strings exist to prevent.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class AgendaReadbackTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 14, 30, 0);
    private AppLanguage original;

    [TestInitialize]
    public void Setup() => original = LocalizationService.Instance.Language;

    [TestCleanup]
    public void Restore() => LocalizationService.Instance.SetLanguage(original);

    [TestMethod]
    public void A_day_on_its_own_is_a_deadline_or_an_all_day_event()
    {
        AgendaReadbackLines ko = Describe("내일", AppLanguage.Korean);
        Assert.AreEqual("10월 8일 (목) 마감", ko.Task);
        Assert.AreEqual("10월 8일 (목) · 하루 종일", ko.Event);
        Assert.AreEqual(string.Empty, ko.Note);

        AgendaReadbackLines en = Describe("tomorrow", AppLanguage.English);
        Assert.AreEqual("Due Thu, Oct 8", en.Task);
        Assert.AreEqual("Thu, Oct 8 · All day", en.Event);
    }

    [TestMethod]
    public void A_time_puts_the_meridiem_where_the_language_wants_it()
    {
        // The two mocks differ in more than words: Korean says "오후" once at the front and joins
        // the date with a space, English says "PM" once at the end and joins with "·".
        AgendaReadbackLines ko = Describe("금요일 3시", AppLanguage.Korean);
        Assert.AreEqual("10월 9일 (금) 오후 3:00 마감", ko.Task);
        Assert.AreEqual("10월 9일 (금) 오후 3:00–4:00", ko.Event);

        AgendaReadbackLines en = Describe("fri 3pm", AppLanguage.English);
        Assert.AreEqual("Due Fri, Oct 9 at 3:00 PM", en.Task);
        Assert.AreEqual("Fri, Oct 9 · 3:00–4:00 PM", en.Event);
    }

    [TestMethod]
    public void A_repeat_spells_the_weekday_out_in_korean_and_abbreviates_it_in_english()
    {
        // A habit of the language, not a shortage of room: both mocks are the wide one.
        AgendaReadbackLines ko = Describe("매주 월 7시", AppLanguage.Korean);
        Assert.AreEqual("매주 월요일 오전 7:00", ko.Task);
        Assert.AreEqual("매주 월요일 오전 7:00–8:00", ko.Event);

        AgendaReadbackLines en = Describe("every mon 7am", AppLanguage.English);
        Assert.AreEqual("Every Mon at 7:00 AM", en.Task);
        Assert.AreEqual("Every Mon · 7:00–8:00 AM", en.Event);
    }

    [TestMethod]
    public void A_repeat_the_app_can_schedule_says_nothing_about_alerts()
    {
        // It used to say "repeating to-dos don't send alerts yet" about every rule. Daily and
        // weekly ones do now, and a blanket warning is a lie the user could disprove in a minute.
        Assert.AreEqual(string.Empty, Describe("매주 월 7시", AppLanguage.Korean).Note);
        Assert.AreEqual(string.Empty, Describe("every mon 7am", AppLanguage.English).Note);
    }

    [TestMethod]
    public void A_time_read_as_tomorrow_says_so_twice()
    {
        // Once in the line, because the eye reads the bold text first and may never reach the
        // note; once in the note, with what to type instead.
        AgendaReadbackLines ko = Describe("9시", AppLanguage.Korean);
        Assert.AreEqual("내일 10월 8일 (목) 오전 9:00 마감", ko.Task);
        Assert.AreEqual("오전 9시가 지나 내일로 읽었어요. 오늘 밤이면 ‘21시’로 입력하세요.", ko.Note);

        AgendaReadbackLines en = Describe("9", AppLanguage.English);
        Assert.AreEqual("Due Tomorrow, Thu Oct 8 at 9:00 AM", en.Task);
        Assert.AreEqual("9 AM has passed, so this reads as tomorrow. Type ‘9pm’ for tonight.", en.Note);
    }

    [TestMethod]
    public void The_phone_bar_shortens_the_same_answer()
    {
        // Never a different reading — that is the thing that would make the two shells disagree in
        // front of the same user. Only fewer characters.
        AgendaReadbackLines ko = Describe("10/8 9시", AppLanguage.Korean, ReadbackWidth.Compact);
        Assert.AreEqual("10/8 (목) 오전 9:00 마감", ko.Task);
        Assert.AreEqual("10/8 (목) 오전 9–10시", ko.Event);

        AgendaReadbackLines en = Describe("10/8 9am", AppLanguage.English, ReadbackWidth.Compact);
        Assert.AreEqual("Due Oct 8 at 9:00 AM", en.Task);
        Assert.AreEqual("Oct 8 · 9–10 AM", en.Event);
    }

    [TestMethod]
    public void A_range_that_crosses_noon_says_the_meridiem_on_both_ends()
    {
        // Saying it once is a shorthand that only works while both ends agree. 11:30 to 12:30 is
        // the case where the shorthand would be a lie.
        AgendaReadbackLines ko = Describe("내일 11:30", AppLanguage.Korean);
        Assert.AreEqual("10월 8일 (목) 오전 11:30–오후 12:30", ko.Event);

        AgendaReadbackLines en = Describe("tomorrow 11:30", AppLanguage.English);
        Assert.AreEqual("Thu, Oct 8 · 11:30 AM–12:30 PM", en.Event);
    }

    [TestMethod]
    public void The_empty_prompt_invites_rather_than_complains()
    {
        LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
        Assert.AreEqual("날짜·시간·반복을 입력하세요", AgendaReadback.EmptyPrompt(ReadbackWidth.Full));
        // The phone's examples are tappable chips, so its wording says so.
        Assert.AreEqual("날짜·시간·반복을 입력하거나 고르세요", AgendaReadback.EmptyPrompt(ReadbackWidth.Compact));
        CollectionAssert.AreEqual(
            new[] { "오늘", "내일", "금요일 3시", "매주 월" },
            AgendaReadback.Examples.ToArray());

        LocalizationService.Instance.SetLanguage(AppLanguage.English);
        Assert.AreEqual("Type a date, time or repeat", AgendaReadback.EmptyPrompt(ReadbackWidth.Full));
        CollectionAssert.AreEqual(
            new[] { "today", "tomorrow", "fri 3pm", "every mon" },
            AgendaReadback.Examples.ToArray());
    }

    [TestMethod]
    public void Every_example_chip_is_something_the_parser_can_read()
    {
        // A hint the app itself cannot honour is worse than no hint.
        foreach (AppLanguage language in new[] { AppLanguage.Korean, AppLanguage.English })
        {
            LocalizationService.Instance.SetLanguage(language);
            foreach (string example in AgendaReadback.Examples)
            {
                Assert.IsNotNull(AgendaPhraseParser.Parse(example, Now), $"{language}: {example}");
            }
        }
    }

    private static AgendaReadbackLines Describe(
        string typed,
        AppLanguage language,
        ReadbackWidth width = ReadbackWidth.Full)
    {
        LocalizationService.Instance.SetLanguage(language);
        AgendaPhrase? phrase = AgendaPhraseParser.Parse(typed, Now);
        Assert.IsNotNull(phrase, $"'{typed}' was not understood.");
        return AgendaReadback.Describe(phrase.Value, width);
    }
}
