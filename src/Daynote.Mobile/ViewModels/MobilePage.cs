namespace Daynote.Mobile.ViewModels;

/// <summary>
/// The phone's top-level destinations, one per bottom-bar tab.
/// </summary>
/// <remarks>
/// The desktop shell has no equivalent: there every surface is on screen at once in three columns.
/// A phone shows one at a time, so "which panel is visible" becomes real navigation state, and the
/// editor is not a tab at all — it opens over the day, as <c>IsEditorOpen</c>.
/// </remarks>
public enum MobilePage
{
    /// <summary>The month grid, and the notes of the day it has selected.</summary>
    Day,

    /// <summary>Unified search over every note.</summary>
    Search,

    /// <summary>To-dos, favourites and tags, as a segmented list.</summary>
    Lists,

    /// <summary>Account, theme, language and the rest.</summary>
    Settings,
}
