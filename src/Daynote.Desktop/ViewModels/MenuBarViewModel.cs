using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Composition;
using Daynote.App.Input;
using Daynote.App.Localization;
using Daynote.App.Notes;
using Daynote.Core.Agenda;
using Daynote.Core.Domain;
using Daynote.Core.Settings;
using Daynote.Core.Time;

namespace Daynote.Desktop.ViewModels;

/// <summary>What the popover's box makes: a to-do, or a line in today's note. Never both.</summary>
public enum MenuBarCaptureMode
{
    Todo,
    Note,
}

/// <summary>What became of a line typed into the popover in 노트 mode.</summary>
public enum MenuBarAppendResult
{
    Appended,
    NewNote,
    Failed,
}

/// <summary>
/// The menu bar popover on the Mac and the tray flyout on Windows (menu bar design §01, Motion M9).
/// </summary>
/// <remarks>
/// One view model for both shells: the date, a capture box, the next event and today's to-dos.
/// <para>
/// <b>Notes and to-dos are never mixed in one input</b> (docs/TODOS.md, 2026-10-10). A switch
/// above the box says what Enter makes: in 할 일 the text is the to-do's title, for today with no
/// time; in 노트 it is a line for today's note — the watch's "노트에 한 줄". Neither reads an
/// <c>@</c>: it is just a character. 자세히… opens the app's full add card for anything more.
/// </para>
/// <para>
/// <b>It stays open after Enter.</b> M9: what was made has to be seen arriving, so a to-do for
/// today rises to the top of the list as 방금, and one for another day says where it went.
/// </para>
/// </remarks>
public sealed partial class MenuBarViewModel : ObservableObject, ILanguageAware
{
    private readonly IAgendaRepository agenda;
    private readonly IClock clock;
    private readonly ToggleAgendaItem toggle;
    private readonly Func<string, bool, Task<MenuBarAppendResult>> appendLine;
    private readonly Action<LocalDate?> openApp;
    private readonly Action openSettings;
    private readonly Func<string, Task> openTodoForm;
    private readonly ISettingsStore settings;
    private readonly List<Guid> justAdded = [];
    private IReadOnlyList<AgendaItem> items = [];
    private IReadOnlyDictionary<Guid, int> tones = new Dictionary<Guid, int>();
    private string chordText = string.Empty;
    private bool submitting;
    private bool modeLoaded;

    public MenuBarViewModel(
        IAgendaRepository agenda,
        IClock clock,
        ISettingsStore settings,
        Func<string, bool, Task<MenuBarAppendResult>> appendLine,
        Func<string, Task> openTodoForm,
        Action<LocalDate?> openApp,
        Action openSettings)
    {
        this.agenda = agenda ?? throw new ArgumentNullException(nameof(agenda));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.appendLine = appendLine ?? throw new ArgumentNullException(nameof(appendLine));
        this.openTodoForm = openTodoForm ?? throw new ArgumentNullException(nameof(openTodoForm));
        this.openApp = openApp ?? throw new ArgumentNullException(nameof(openApp));
        this.openSettings = openSettings ?? throw new ArgumentNullException(nameof(openSettings));
        toggle = new ToggleAgendaItem(agenda);
        LocalizationService.Instance.Observe(this);
    }

    /// <summary>Asks the host to close the popover: Esc with nothing to dismiss.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>A to-do or event was made or ticked here, so the window's panels should re-read.</summary>
    public event EventHandler? AgendaChanged;

    public ObservableCollection<MenuBarTodoRowViewModel> Todos { get; } = [];

    /// <summary>True while the popover is on screen. A refresh then keeps ticked rows in place.</summary>
    public bool IsOpen { get; private set; }

    [ObservableProperty]
    public partial string Draft { get; set; } = string.Empty;

    /// <summary>What Enter makes. 할 일 until the user picks 노트; the last choice is kept.</summary>
    [ObservableProperty]
    public partial MenuBarCaptureMode Mode { get; private set; } = MenuBarCaptureMode.Todo;

    public bool IsTodoMode => Mode == MenuBarCaptureMode.Todo;

    public bool IsNoteMode => Mode == MenuBarCaptureMode.Note;

    /// <summary>Today's to-dos still owed; the number beside the status item.</summary>
    [ObservableProperty]
    public partial int RemainingCount { get; private set; }

    [ObservableProperty]
    public partial string HeaderText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasNextEvent { get; private set; }

    [ObservableProperty]
    public partial string NextEventTitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string NextEventDetail { get; private set; } = string.Empty;

    /// <summary>The one line under the box after something was made or added, or empty.</summary>
    [ObservableProperty]
    public partial string NoticeText { get; private set; } = string.Empty;

    /// <summary>Where 보기 goes, or null when the notice offers nowhere to go.</summary>
    [ObservableProperty]
    public partial LocalDate? NoticeDate { get; private set; }

    public bool HasNotice => NoticeText.Length > 0;

    public bool HasNoticeAction => NoticeDate is not null;

    /// <summary>The quick-capture chord as the platform writes it, for the header.</summary>
    public string ChordText
    {
        get => chordText;
        set => SetProperty(ref chordText, value);
    }

    public string Placeholder => IsTodoMode ? AppStrings.MenuBarTodoPlaceholder : AppStrings.MenuBarNotePlaceholder;

    public string TodoModeLabel => AppStrings.MenuBarModeTodo;

    public string NoteModeLabel => AppStrings.MenuBarModeNote;

    public string MoreLabel => AppStrings.MenuBarMore;

    public string UpNextLabel => AppStrings.MenuBarUpNext;

    public string TodosHeader => string.Format(
        CultureInfo.CurrentCulture, AppStrings.MenuBarTodosFormat, RemainingCount);

    public string TodosEmpty => AppStrings.MenuBarTodosEmpty;

    public bool HasNoTodos => Todos.Count == 0;

    public string JustNowLabel => AppStrings.MenuBarJustNow;

    public string ViewLabel => AppStrings.MenuBarView;

    public string OpenAppLabel => AppStrings.MenuBarOpenApp;

    public string SettingsLabel => AppStrings.MenuBarSettings;

    /// <summary>Text in the box: the line under it says where Enter will put it.</summary>
    public bool IsHintVisible => Draft.Trim().Length > 0;

    public string Hint => IsTodoMode
        ? AppStrings.MenuBarTodoHint
        : string.Format(
            CultureInfo.CurrentCulture,
            AppStrings.MenuBarAppendHintFormat,
            OperatingSystem.IsMacOS() ? "⌘Enter" : "Ctrl+Enter");

    private DateTime Now
    {
        get
        {
            ClockSnapshot snapshot = clock.Read();
            return snapshot.UtcInstant.ToOffset(snapshot.LocalUtcOffset).DateTime;
        }
    }

    /// <summary>The popover is about to show: a fresh read, and nothing left over from last time.</summary>
    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        IsOpen = true;
        justAdded.Clear();
        ClearNotice();
        if (!modeLoaded)
        {
            modeLoaded = true;
            string? stored = await settings.GetAsync(ShortcutSettings.MenuBarCaptureModeKey, cancellationToken).ConfigureAwait(true);
            Mode = Enum.TryParse(stored, ignoreCase: true, out MenuBarCaptureMode mode) ? mode : MenuBarCaptureMode.Todo;
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// The popover went away. What was typed stays, so a click elsewhere does not cost a sentence;
    /// the 방금 marks do not, because they are news only once.
    /// </summary>
    public void Close()
    {
        IsOpen = false;
        justAdded.Clear();
        ClearNotice();
        Rebuild();
    }

    /// <summary>Re-reads every to-do and event, then rebuilds what the popover shows.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        items = await agenda.GetAllAsync(cancellationToken).ConfigureAwait(true);
        IReadOnlyList<AgendaList> lists = await agenda.GetListsAsync(cancellationToken).ConfigureAwait(true);
        tones = Tones(lists);
        Rebuild();
    }

    /// <summary>
    /// Rebuilds from what was last read, against the clock now: the date rolls over at midnight and
    /// a time goes red the minute it passes, neither of which is a change anyone writes.
    /// </summary>
    public void Rebuild()
    {
        DateTime now = Now;
        DateOnly today = DateOnly.FromDateTime(now);
        HeaderText = today.ToString(AppStrings.MenuBarHeaderFormat, LocalizationService.Instance.Culture);

        AgendaDayView day = AgendaDay.For(today, items);
        BuildNextEvent(day, now);

        AgendaDayRow[] open = [.. day.Open.Where(static row => row.Item.Kind == AgendaKind.Task)];

        // Made here since the popover opened: newest first, above everything, whatever its time.
        // Ticked ones stay put while the popover is up, for the same reason they are not removed.
        var rows = new List<MenuBarTodoRowViewModel>();
        foreach (Guid id in Enumerable.Reverse(justAdded))
        {
            if (open.FirstOrDefault(row => row.Item.Id == id) is { Item: not null } made)
            {
                rows.Add(Row(made, isJustAdded: true, now));
            }
        }

        foreach (AgendaDayRow row in open)
        {
            if (!justAdded.Contains(row.Item.Id))
            {
                rows.Add(Row(row, isJustAdded: false, now));
            }
        }

        if (IsOpen)
        {
            // A row ticked a moment ago is done in the store and gone from `open`; keep it showing.
            foreach (MenuBarTodoRowViewModel ticked in Todos.Where(static row => row.IsDone))
            {
                int at = Math.Min(Todos.IndexOf(ticked), rows.Count);
                rows.Insert(at, ticked);
            }
        }

        Todos.Clear();
        foreach (MenuBarTodoRowViewModel row in rows)
        {
            Todos.Add(row);
        }

        RecountRemaining();
        OnPropertyChanged(nameof(HasNoTodos));
    }

    partial void OnDraftChanged(string value)
    {
        if (value.Length > 0)
        {
            ClearNotice();
        }

        OnPropertyChanged(nameof(IsHintVisible));
    }

    partial void OnModeChanged(MenuBarCaptureMode value)
    {
        OnPropertyChanged(nameof(IsTodoMode));
        OnPropertyChanged(nameof(IsNoteMode));
        OnPropertyChanged(nameof(Placeholder));
        OnPropertyChanged(nameof(Hint));
    }

    /// <summary>The switch above the box, or Tab. Kept for next time; what was typed stays.</summary>
    public async Task SetModeAsync(MenuBarCaptureMode mode)
    {
        if (Mode == mode)
        {
            return;
        }

        Mode = mode;
        try
        {
            await settings.SetAsync(ShortcutSettings.MenuBarCaptureModeKey, mode.ToString()).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The switch still works for this session; only remembering it failed.
            System.Diagnostics.Trace.TraceError(exception.ToString());
        }
    }

    /// <summary>Tab: the other mode.</summary>
    public Task ToggleModeAsync() =>
        SetModeAsync(IsTodoMode ? MenuBarCaptureMode.Note : MenuBarCaptureMode.Todo);

    /// <summary>
    /// Enter (or ⌘Enter for a new note in 노트). 할 일 makes a to-do for today titled with the text
    /// as typed; 노트 puts the line into today's note. Returns whether anything was written. The
    /// popover stays open either way.
    /// </summary>
    /// <remarks>
    /// One at a time. Enter is posted on every key-down, auto-repeat included, and a second press
    /// arriving while the first is still writing would make the same to-do, or add the same line,
    /// twice.
    /// </remarks>
    public async Task<bool> SubmitAsync(bool newNote = false, CancellationToken cancellationToken = default)
    {
        string text = Draft;
        if (submitting || text.Trim().Length == 0)
        {
            return false;
        }

        submitting = true;
        try
        {
            return IsTodoMode
                ? await MakeAsync(text, cancellationToken).ConfigureAwait(true)
                : await AppendAsync(text, newNote).ConfigureAwait(true);
        }
        finally
        {
            submitting = false;
        }
    }

    /// <summary>A to-do for today with no time, titled with the text: written and announced.</summary>
    private async Task<bool> MakeAsync(string text, CancellationToken cancellationToken)
    {
        DateOnly today = DateOnly.FromDateTime(Now);
        var reading = new AgendaPhrase(
            new WallClock(today.ToDateTime(TimeOnly.MinValue)), HasTime: false, Rrule: null, RolledToTomorrow: false, Length: 0);
        var state = new AgendaCaptureState(0, 0, text.Trim(), string.Empty, reading);

        // Typed here rather than in a note, so there is no note to jump back to.
        AgendaItem made = AgendaCapture.Compose(state, AgendaKind.Task, Guid.Empty, Guid.NewGuid(), clock.Read().UtcInstant)
            with { SourceNoteId = null };
        try
        {
            await agenda.SaveAsync(made, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The text stays, so Enter again is the retry.
            ShowNotice(AppStrings.MenuBarSaveFailed, null);
            System.Diagnostics.Trace.TraceError(exception.ToString());
            return false;
        }

        AgendaChanged?.Invoke(this, EventArgs.Empty);
        Draft = string.Empty;
        await AnnounceAsync(made, cancellationToken).ConfigureAwait(true);
        return true;
    }

    /// <summary>A plain line into today's note, through the host.</summary>
    private async Task<bool> AppendAsync(string text, bool newNote)
    {
        MenuBarAppendResult result;
        try
        {
            result = await appendLine(text.Trim(), newNote).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            System.Diagnostics.Trace.TraceError(exception.ToString());
            result = MenuBarAppendResult.Failed;
        }

        if (result == MenuBarAppendResult.Failed)
        {
            ShowNotice(AppStrings.MenuBarAppendFailed, null);
            return false;
        }

        Draft = string.Empty;
        ShowNotice(
            result == MenuBarAppendResult.NewNote ? AppStrings.MenuBarAppendedNewNote : AppStrings.MenuBarAppended,
            LocalDates.FromDateOnly(DateOnly.FromDateTime(Now)));
        return true;
    }

    /// <summary>Esc: asks for the popover to close. What was typed stays for next time.</summary>
    public void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ViewNotice()
    {
        if (NoticeDate is { } date)
        {
            ClearNotice();
            openApp(date);
        }
    }

    [RelayCommand]
    private void OpenApp() => openApp(null);

    [RelayCommand]
    private void OpenSettings() => openSettings();

    [RelayCommand]
    private Task SelectTodoMode() => SetModeAsync(MenuBarCaptureMode.Todo);

    [RelayCommand]
    private Task SelectNoteMode() => SetModeAsync(MenuBarCaptureMode.Note);

    /// <summary>
    /// 자세히…: the app's add card — kind, date, time, repeat, description, list — on today, with
    /// what was typed as its title. The text moves there, so it is not made twice.
    /// </summary>
    [RelayCommand]
    private async Task OpenTodoForm()
    {
        string title = Draft.Trim();
        Draft = string.Empty;
        await openTodoForm(title).ConfigureAwait(true);
    }

    /// <summary>
    /// Says what was made. A to-do for today shows itself at the top of the list; anything for
    /// another day says where it went, because that is the case where looking at today would not
    /// find it (M4).
    /// </summary>
    private async Task AnnounceAsync(AgendaItem made, CancellationToken cancellationToken)
    {
        DateOnly today = DateOnly.FromDateTime(Now);
        DateOnly? day = made.Anchor is { } anchor ? DateOnly.FromDateTime(anchor.Value) : null;
        if (made.Kind == AgendaKind.Task && day == today)
        {
            justAdded.Add(made.Id);
            ClearNotice();
        }
        else if (day is { } other && other != today)
        {
            ShowNotice(
                string.Format(
                    CultureInfo.CurrentCulture,
                    AppStrings.MenuBarAddedToFormat,
                    other.ToString(AppStrings.MenuBarNoticeDateFormat, LocalizationService.Instance.Culture)),
                LocalDates.FromDateOnly(other));
        }
        else
        {
            ClearNotice();
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <remarks>
    /// The ring fills at once and is taken back if the store refuses: a tick that waited on a
    /// write would feel like a missed click, and one that stayed after a failed write would lie.
    /// </remarks>
    private async Task ToggleRowAsync(MenuBarTodoRowViewModel row)
    {
        bool wasDone = row.IsDone;
        bool wasTicked = row.IsTickedHere;
        row.IsDone = !wasDone;
        row.IsTickedHere = row.IsDone;
        RecountRemaining();

        try
        {
            AgendaItem updated = await toggle.ToggleAsync(row.Row).ConfigureAwait(true);
            row.Row = new AgendaDayRow(updated, row.Row.RecurrenceId, row.Row.At);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            row.IsDone = wasDone;
            row.IsTickedHere = wasTicked;
            RecountRemaining();
            ShowNotice(AppStrings.MenuBarSaveFailed, null);
            System.Diagnostics.Trace.TraceError(exception.ToString());
            return;
        }

        AgendaChanged?.Invoke(this, EventArgs.Empty);

        // Re-read underneath without rebuilding: the store has the tick, the list keeps the row.
        try
        {
            items = await agenda.GetAllAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The tick is written; the next opening reads everything again anyway.
            System.Diagnostics.Trace.TraceError(exception.ToString());
        }
    }

    private MenuBarTodoRowViewModel Row(AgendaDayRow row, bool isJustAdded, DateTime now) =>
        new(row, tones.GetValueOrDefault(row.Item.ListId), isJustAdded, now, ToggleRowAsync);

    private void BuildNextEvent(AgendaDayView day, DateTime now)
    {
        // Today's events not yet over, soonest first. One that has started is still "next" until
        // it ends: it is the thing the user is, or should be, in.
        AgendaDayRow? next = day.Open.Concat(day.Done)
            .Where(static row => row.Item.Kind == AgendaKind.Event && row.At is not null)
            .Where(row => End(row) > now)
            .OrderBy(static row => row.At!.Value.Value)
            .Cast<AgendaDayRow?>()
            .FirstOrDefault();

        HasNextEvent = next is not null;
        if (next is not { } shown)
        {
            NextEventTitle = string.Empty;
            NextEventDetail = string.Empty;
            return;
        }

        CultureInfo culture = CultureInfo.CurrentCulture;
        DateTime start = shown.At!.Value.Value;
        string span = start.ToString(AppStrings.TodoRowTimeFormat, culture);
        if (EndsAt(shown) is { } end)
        {
            span += "–" + end.ToString(AppStrings.TodoRowTimeFormat, culture);
        }

        string when = start <= now
            ? AppStrings.MenuBarEventNow
            : string.Format(culture, AppStrings.MenuBarEventInFormat, Duration(start - now));

        NextEventTitle = shown.Item.Title;
        NextEventDetail = span + " · " + when;
    }

    /// <summary>When an occurrence ends: its own end, moved with it if it is one of a rule's.</summary>
    private static DateTime? EndsAt(AgendaDayRow row)
    {
        if (row.Item.EndsAt is not { } end || row.At is not { } at)
        {
            return null;
        }

        TimeSpan length = end.Value - (row.Item.StartsAt?.Value ?? end.Value);
        return at.Value + (length < TimeSpan.Zero ? TimeSpan.Zero : length);
    }

    private static DateTime End(AgendaDayRow row) => EndsAt(row) ?? row.At!.Value.Value;

    /// <summary>"1시간 30분" / "1 h 30 min", rounded up so "in 0 min" is never said.</summary>
    public static string Duration(TimeSpan until)
    {
        int minutes = Math.Max(1, (int)Math.Ceiling(until.TotalMinutes));
        int hours = minutes / 60;
        int rest = minutes % 60;
        CultureInfo culture = CultureInfo.CurrentCulture;
        return hours > 0 && rest > 0 ? string.Format(culture, AppStrings.MenuBarHoursMinutesFormat, hours, rest)
            : hours > 0 ? string.Format(culture, AppStrings.MenuBarHoursFormat, hours)
            : string.Format(culture, AppStrings.MenuBarMinutesFormat, rest);
    }

    /// <summary>
    /// A chord as the header shows it: ⌥⌘Space on the Mac, in the order the menu bar writes
    /// modifiers, and the stored form (Ctrl+Alt+Space) elsewhere. Empty when there is none.
    /// </summary>
    public static string FormatChord(Hotkey? hotkey)
    {
        if (hotkey is not { } chord)
        {
            return string.Empty;
        }

        string text = chord.ToDisplayString();
        if (!OperatingSystem.IsMacOS())
        {
            return text;
        }

        string key = text.EndsWith("++", StringComparison.Ordinal) ? "+" : text[(text.LastIndexOf('+') + 1)..];
        return (chord.Modifiers.HasFlag(HotkeyModifiers.Control) ? "⌃" : string.Empty)
            + (chord.Modifiers.HasFlag(HotkeyModifiers.Alt) ? "⌥" : string.Empty)
            + (chord.Modifiers.HasFlag(HotkeyModifiers.Shift) ? "⇧" : string.Empty)
            + (chord.Modifiers.HasFlag(HotkeyModifiers.Meta) ? "⌘" : string.Empty)
            + key;
    }

    /// <summary>
    /// The ring colour of each list: the built-in one takes the sun, the rest alternate after it.
    /// </summary>
    internal static IReadOnlyDictionary<Guid, int> Tones(IReadOnlyList<AgendaList> lists)
    {
        var tones = new Dictionary<Guid, int>();
        int others = 0;
        foreach (AgendaList list in lists)
        {
            tones[list.Id] = list.IsDefault ? 0 : 1 + (others++ % 2);
        }

        return tones;
    }

    private void RecountRemaining()
    {
        RemainingCount = Todos.Count(static row => !row.IsDone);
        OnPropertyChanged(nameof(TodosHeader));
    }

    private void ShowNotice(string text, LocalDate? date)
    {
        NoticeText = text;
        NoticeDate = date;
    }

    private void ClearNotice() => ShowNotice(string.Empty, null);

    partial void OnNoticeTextChanged(string value) => OnPropertyChanged(nameof(HasNotice));

    partial void OnNoticeDateChanged(LocalDate? value) => OnPropertyChanged(nameof(HasNoticeAction));

    void ILanguageAware.OnLanguageChanged()
    {
        Rebuild();
        OnPropertyChanged(string.Empty);
    }
}
