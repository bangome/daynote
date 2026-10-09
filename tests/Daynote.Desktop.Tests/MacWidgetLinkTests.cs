using Daynote.Desktop.Platform;

namespace Daynote.Desktop.Tests;

/// <summary>
/// The links a Mac widget opens the app with (docs/APPLE_EXTENSIONS.md §7): the phone's
/// <c>daynote://</c> scheme, of which the desktop follows the two that name a day.
/// </summary>
[TestClass]
public sealed class MacWidgetLinkTests
{
    [TestMethod]
    public void A_day_link_and_a_note_link_open_their_day()
    {
        Assert.AreEqual("2026-10-07", MacWidgetBridge.DayFromLink(new Uri("daynote://day?date=2026-10-07"))?.ToString());
        Assert.AreEqual(
            "2026-10-05",
            MacWidgetBridge.DayFromLink(new Uri("daynote://note?date=2026-10-05&id=8a3c1e4e-0f6b-4c55-9d8e-1a2b3c4d5e01"))?.ToString());
    }

    [TestMethod]
    public void Anything_else_names_no_day()
    {
        Assert.IsNull(MacWidgetBridge.DayFromLink(new Uri("daynote://day?date=yesterday")));
        Assert.IsNull(MacWidgetBridge.DayFromLink(new Uri("daynote://todos")));
        Assert.IsNull(MacWidgetBridge.DayFromLink(new Uri("daynote://capture?at=1")));
        Assert.IsNull(MacWidgetBridge.DayFromLink(new Uri("https://day?date=2026-10-07")));
        Assert.IsNull(MacWidgetBridge.DayFromLink(null));
    }
}
