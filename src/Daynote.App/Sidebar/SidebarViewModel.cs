using CommunityToolkit.Mvvm.Input;
using Daynote.App.Composition;
using Daynote.App.Notes;
using Daynote.Core.Time;

namespace Daynote.App.Sidebar;

/// <summary>
/// The sidebar navigation region: a Today shortcut, the selected date's note list (mirroring the
/// note tabs), an add-note command, and the bottom-docked mini calendar (DESIGN Section 5).
/// </summary>
public sealed class SidebarViewModel
{
    private readonly IDateNavigator _navigator;
    private readonly IClock _clock;

    public SidebarViewModel(IDateNavigator navigator, NoteWorkspaceViewModel notes, IClock clock)
    {
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        Notes = notes ?? throw new ArgumentNullException(nameof(notes));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Calendar = new MiniCalendarViewModel(navigator, clock);
        GoToTodayCommand = new AsyncRelayCommand(GoToTodayAsync);
        SelectNoteCommand = new AsyncRelayCommand<NoteTabViewModel>(tab => Notes.SelectNoteAsync(tab));
        AddNoteCommand = new AsyncRelayCommand(() => Notes.AddNoteAsync());
    }

    public NoteWorkspaceViewModel Notes { get; }

    public MiniCalendarViewModel Calendar { get; }

    public IAsyncRelayCommand GoToTodayCommand { get; }

    public IAsyncRelayCommand<NoteTabViewModel> SelectNoteCommand { get; }

    public IAsyncRelayCommand AddNoteCommand { get; }

    private Task GoToTodayAsync() => _navigator.SelectDateAsync(LocalDates.Today(_clock));
}
