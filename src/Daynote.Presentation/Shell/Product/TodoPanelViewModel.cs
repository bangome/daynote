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

        TodayItems.Clear();
        Items.Clear();
        foreach (AgendaDayRow row in owed.Today)
        {
            TodayItems.Add(Row(row, now.DateTime));
        }

        // Later and then undated, in that order: the design puts "날짜 없음" last because a to-do
        // with no day is the one you are least likely to be looking for.
        foreach (AgendaDayRow row in owed.Later.Concat(owed.Undated))
        {
            Items.Add(Row(row, now.DateTime));
        }

        OpenCount = owed.Count;
        HasToday = TodayItems.Count > 0;
        IsEmpty = TodayItems.Count == 0 && Items.Count == 0;
        Refreshed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Builds one row, in whichever scope the caller is showing.</summary>
    public TodoItemViewModel Row(AgendaDayRow row, DateTime now, TodoRowScope scope = TodoRowScope.AllDates) =>
        new(row, scope, listNames.GetValueOrDefault(row.Item.ListId, string.Empty), now, onToggle, onJump);
}
