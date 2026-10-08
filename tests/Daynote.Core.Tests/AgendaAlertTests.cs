using Daynote.Core.Agenda;

namespace Daynote.Core.Tests;

/// <summary>
/// The alerts an item carries (docs/design-renewal/Daynote Mobile B Tasks - Events 06 Alerts).
/// </summary>
[TestClass]
public sealed class AgendaAlertTests
{
    [TestMethod]
    public void A_new_dated_item_starts_with_one_alert_and_none_is_expressible()
    {
        // The whole point of §06: the behaviour every dated to-do has had since reminders shipped
        // survives as a default the user can see and remove, instead of being a rule hidden in
        // the planner that nothing could turn off.
        CollectionAssert.AreEqual(new[] { AgendaAlert.AtTime }, AgendaAlert.Default.ToArray());
        Assert.IsEmpty(AgendaAlert.None);
    }

    [TestMethod]
    public void The_sheet_offers_its_leads_in_the_order_it_lists_them()
    {
        CollectionAssert.AreEqual(new[] { 0, 5, 10, 30, 60 }, AgendaAlert.Offered.ToArray());
    }

    [TestMethod]
    public void The_day_before_at_nine_is_a_lead_like_any_other()
    {
        // iCalendar has one kind of trigger — a duration before the item — so "the day before at
        // nine" is just a long one, and it depends on what time the item is at.
        Assert.AreEqual(
            29 * 60,
            AgendaAlert.DayBefore(new TimeOnly(14, 0), new TimeOnly(9, 0)));
        Assert.AreEqual(
            24 * 60,
            AgendaAlert.DayBefore(new TimeOnly(9, 0), new TimeOnly(9, 0)));
    }

    [TestMethod]
    public void Adding_keeps_the_list_ordered_from_the_earliest_warning_to_the_item()
    {
        IReadOnlyList<int> leads = AgendaAlert.With(AgendaAlert.Default, 30);
        leads = AgendaAlert.With(leads, 5);

        // Two orders of the same set would look like two different states in a list the sheet
        // draws top to bottom.
        CollectionAssert.AreEqual(new[] { 30, 5, 0 }, leads.ToArray());
    }

    [TestMethod]
    public void Adding_one_that_is_already_there_changes_nothing()
    {
        // The sheet marks it "추가됨" rather than offering it again, and two identical alerts
        // would be two identical notifications.
        IReadOnlyList<int> leads = AgendaAlert.With(AgendaAlert.Default, AgendaAlert.AtTime);

        CollectionAssert.AreEqual(new[] { 0 }, leads.ToArray());
    }

    [TestMethod]
    public void Removing_the_last_one_leaves_no_alert_rather_than_the_default()
    {
        IReadOnlyList<int> leads = AgendaAlert.Without(AgendaAlert.Default, AgendaAlert.AtTime);

        Assert.IsEmpty(leads);
    }
}
