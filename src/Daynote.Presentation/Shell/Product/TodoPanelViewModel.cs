using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Daynote.App.Localization;
using Daynote.Core.Agenda;
using Daynote.Core.Time;

namespace Daynote.App.Shell.Product;

/// <summary>
/// The 할 일 tab: everything still owed, across every date (design §04, 4a).
/// </summary>
/// <remarks>
/// It used to parse <c>-[]</c> lines out of every note body. It now reads to-do entities, and
/// which rows exist and in what order is <see cref="AgendaOutstanding"/>'s decision rather than
/// this class's — the phone's list has to agree with it, and two implementations of "what is
/// owed" is how they stop agreeing.
/// <para>
/// A repeating to-do appears once, as its next outstanding occurrence. Finished ones are absent:
/// this is a queue, not a record.
/// </para>
/// </remarks>
public sealed partial class TodoPanelViewModel : ObservableObject, ILanguageAware
{
    private readonly IAgendaRepository agenda;
    private readonly IClock clock;
    private readonly Func<AgendaDayRow, Task> onToggle;
    private readonly Func<AgendaDayRow, Task> onJump;
    private IReadOnlyDictionary<Guid, string> listNames = new Dictionary<Guid, string>();

    public TodoPanelViewModel(
        IAgendaRepository agenda,
        IClock clock,
        Func<AgendaDayRow, Task> onToggle,
        Func<AgendaDayRow, Task> onJump)
    {
        this.agenda = agenda ?? throw new ArgumentNullException(nameof(agenda));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.onToggle = onToggle ?? throw new ArgumentNullException(nameof(onToggle));
        this.onJump = onJump ?? throw new ArgumentNullException(nameof(onJump));
        LocalizationService.Instance.Observe(this);
    }

    /// <summary>Everything visible here is catalog-derived, so re-read every binding.</summary>
    void ILanguageAware.OnLanguageChanged() => OnPropertyChanged(string.Empty);

    /// <summary>What is owed today, or was owed before today and still is.</summary>
    public ObservableCollection<TodoItemViewModel> TodayItems { get; } = [];

    /// <summary>What is ahead, then what carries no day at all.</summary>
    public ObservableCollection<TodoItemViewModel> Items { get; } = [];

    /// <summary>
    /// Everything loaded, so the day panel can project one day out of it without reading again.
    /// </summary>
    public IReadOnlyList<AgendaItem> All { get; private set; } = [];

    /// <summary>List id to display name, for a row's label and for the sidebar's counts.</summary>
    public IReadOnlyDictionary<Guid, string> ListNames => listNames;

    /// <summary>
    /// The lists, with how much is owed in each. The sidebar's rows and the phone's chips, from
    /// one count so the two screens cannot disagree (phone §03).
    /// </summary>
    public ObservableCollection<AgendaListRowViewModel> Lists { get; } = [];

    /// <summary>
    /// What the cross-date view is showing: everything owed, or the one list it is narrowed to.
    /// </summary>
    /// <remarks>
    /// The phone builds its own bands out of these rows rather than reading <see cref="TodayItems"/>,
    /// because it groups them differently. Handing it the filtered view rather than letting it
    /// filter again is what stops a chip and the sidebar row beside it disagreeing.
    /// </remarks>
    public AgendaOutstandingView Outstanding { get; private set; } = new([], [], []);

    /// <summary>
    /// Which list the cross-date view is narrowed to, or null for all of them.
    /// </summary>
    /// <remarks>
    /// A list is a filter, not a destination (§04, phone §03). Selecting one narrows what is
    /// already on screen rather than navigating anywhere, which is why the same list can be
    /// selected from either shell's very different furniture.
    /// </remarks>
    [ObservableProperty]
    private Guid? _selectedListId;

    [ObservableProperty]
    private int _openCount;

    [ObservableProperty]
    private bool _hasToday;

    [ObservableProperty]
    private bool _isEmpty = true;

    /// <summary>Tab header label "할 일 (N)"; recomputed whenever the open count changes.</summary>
    public string TabLabel => string.Format(
        System.Globalization.CultureInfo.CurrentCulture, AppStrings.TabTodoFormat, OpenCount);

    partial void OnOpenCountChanged(int value) => OnPropertyChanged(nameof(TabLabel));

    /// <summary>
    /// Raised once a refresh has rebuilt the lists. Views that derive from them — the desktop's
    /// per-day panel — read the settled state here instead of reacting to every row as it is added.
    /// </summary>
    public event EventHandler? Refreshed;

    /// <summary>Re-reads every to-do. Called on load and after anything changes one.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ClockSnapshot snapshot = clock.Read();
        DateTimeOffset now = snapshot.UtcInstant.ToOffset(snapshot.LocalUtcOffset);

        All = await agenda.GetAllAsync(cancellationToken).ConfigureAwait(true);
        IReadOnlyList<AgendaList> lists = await agenda.GetListsAsync(cancellationToken).ConfigureAwait(true);
        listNames = lists
            .Where(static list => !list.HasBuiltInName)
            .ToDictionary(static list => list.Id, static list => list.Name);

        AgendaOutstandingView owed = AgendaOutstanding.For(DateOnly.FromDateTime(now.DateTime), All);

        // The lists first, because selecting one that has since been deleted has to fall back to
        // "all" before the rows below are filtered by it.
        Lists.Clear();
        foreach (AgendaListRow list in AgendaListCounts.For(lists, owed))
        {
            Lists.Add(new AgendaListRowViewModel(
                list,
                list.List.Id == SelectedListId,
                SelectListAsync,
                RenameListAsync,
                async row => await DeleteListAsync(row.Id).ConfigureAwait(true)));
        }

        if (SelectedListId is { } selected && lists.All(list => list.Id != selected))
        {
            SelectedListId = null;
        }

        Outstanding = SelectedListId is { } only ? Only(owed, only) : owed;
        AgendaOutstandingView shown = Outstanding;

        TodayItems.Clear();
        Items.Clear();
        foreach (AgendaDayRow row in shown.Today)
        {
            TodayItems.Add(Row(row, now.DateTime));
        }

        // Later and then undated, in that order: the design puts "날짜 없음" last because a to-do
        // with no day is the one you are least likely to be looking for.
        foreach (AgendaDayRow row in shown.Later.Concat(shown.Undated))
        {
            Items.Add(Row(row, now.DateTime));
        }

        // The heading counts everything owed, not what the filter leaves: "할 일 17" is how much
        // there is, and narrowing the view does not make seventeen things into seven.
        OpenCount = AgendaListCounts.Total(owed);
        HasToday = TodayItems.Count > 0;
        IsEmpty = TodayItems.Count == 0 && Items.Count == 0;
        Refreshed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Narrows the view to one list, or widens it again when given the selected one.</summary>
    /// <remarks>
    /// Tapping the list you are already in is how a filter is taken off everywhere else, and
    /// there is no other affordance for it on the phone's chip row.
    /// </remarks>
    public async Task SelectListAsync(Guid? id)
    {
        SelectedListId = id == SelectedListId ? null : id;
        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Makes a list and selects it, so the next thing filed goes where it was just made.</summary>
    public async Task<AgendaList> CreateListAsync(string name, CancellationToken cancellationToken = default)
    {
        AgendaList made = await agenda
            .CreateListAsync(Guid.NewGuid(), name, cancellationToken)
            .ConfigureAwait(true);

        SelectedListId = made.Id;
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
        return made;
    }

    public async Task RenameListAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        await agenda.RenameListAsync(id, name, cancellationToken).ConfigureAwait(true);
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Deletes a list. Its to-dos move to the built-in one rather than going with it — a
    /// container is not a reason to lose a task (§3).
    /// </summary>
    public async Task<int> DeleteListAsync(Guid id, CancellationToken cancellationToken = default)
    {
        int moved = await agenda.DeleteListAsync(id, cancellationToken).ConfigureAwait(true) ?? 0;
        if (SelectedListId == id)
        {
            SelectedListId = null;
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(true);
        return moved;
    }

    /// <summary>Only the rows in one list, in the three bands they were already sorted into.</summary>
    private static AgendaOutstandingView Only(AgendaOutstandingView owed, Guid list) => new(
        [.. owed.Today.Where(row => row.Item.ListId == list)],
        [.. owed.Later.Where(row => row.Item.ListId == list)],
        [.. owed.Undated.Where(row => row.Item.ListId == list)]);

    private Task RenameListAsync(Guid id, string name) => RenameListAsync(id, name, CancellationToken.None);

    /// <summary>Builds one row, in whichever scope the caller is showing.</summary>
    public TodoItemViewModel Row(AgendaDayRow row, DateTime now, TodoRowScope scope = TodoRowScope.AllDates) =>
        new(row, scope, listNames.GetValueOrDefault(row.Item.ListId, string.Empty), now, onToggle, onJump);
}
