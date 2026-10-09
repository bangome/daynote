using Daynote.Core.Agenda;

namespace Daynote.Mobile.ViewModels;

/// <summary>Where a tap on a home-screen widget lands.</summary>
public enum WidgetLaunch
{
    /// <summary>The header: the day screen, on today.</summary>
    Today,

    /// <summary>+ 노트: a new note on today, in the editor.</summary>
    NewNote,

    /// <summary>@ 할 일 and the + beside 오늘: the same, with the to-do sheet already up.</summary>
    Capture,
}

/// <summary>
/// The home-screen widgets' side of the shell: what a tap on one opens, and the redraw they need
/// whenever a to-do changes.
/// </summary>
public sealed partial class MobileShellViewModel
{
    /// <summary>
    /// Asks the head to redraw its widgets. Raised from the one place every to-do change passes
    /// through (<see cref="RefreshTodosAsync"/>). Null on a head without widgets. Set by composition.
    /// </summary>
    public Action? AgendaChanged { get; init; }

    /// <summary>A widget was tapped: today, a new note on today, or that note with the to-do sheet open.</summary>
    public async Task OpenFromWidgetAsync(WidgetLaunch launch)
    {
        // The open note is saved before anything moves, as every other way off the editor does.
        await CloseEditorAsync().ConfigureAwait(true);
        if (IsEditorOpen)
        {
            return;
        }

        await GoToTodayPage().ConfigureAwait(true);
        if (launch == WidgetLaunch.Today)
        {
            return;
        }

        await NewNote().ConfigureAwait(true);
        if (launch == WidgetLaunch.Capture)
        {
            await OpenTodoSheet().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// A row ticked on a widget while the app is running: the same tick as the app's own, then a
    /// sync soon, since nobody is going to save a note to set one off.
    /// </summary>
    /// <remarks>
    /// Immediate: a widget has no row to animate and no wait to take back. A tick the app itself is
    /// still holding on the same row (motion spec M3) is dropped, or its write would follow this one
    /// and undo it.
    /// </remarks>
    public async Task ToggleFromWidgetAsync(AgendaDayRow row)
    {
        Ticks.Cancel(Daynote.App.Shell.Product.TodoItemViewModel.KeyOf(row));
        await ToggleTodoAsync(row).ConfigureAwait(true);
        _syncScheduler?.NotifySaved();
    }
}
