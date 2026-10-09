using Avalonia;
using Avalonia.Threading;
using Daynote.App.Composition;
using Daynote.App.Shell.Product;
using Daynote.Core.Domain;
using Daynote.Core.Notes;
using Daynote.Core.Startup;
using Daynote.Desktop.Composition;
using Daynote.Desktop.ViewModels;
using Daynote.Infrastructure.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Desktop.Tests;

/// <summary>
/// The view-model rules the design-B layout added: which view fills the middle, the week strip, the
/// day panel's to-dos, the timeline's day groups, the palette, and the open-at-login switch.
/// </summary>
/// <remarks>
/// The shell is composed from the app's real services over a throwaway database, as the other
/// shell tests do; nothing here draws. The startup service is the one exception, swapped for a fake
/// gateway so the switch can be driven without writing a LaunchAgent into the developer's home.
/// </remarks>
[TestClass]
public sealed class DesignBViewModelTests
{
    // ── Views ────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void A_list_opens_in_the_middle_and_the_sidebar_marks_it()
    {
        WithShell(shell =>
        {
            Wait(shell.ShowListCommand.ExecuteAsync(RightTab.Tags));

            Assert.IsTrue(shell.IsListMode);
            Assert.IsFalse(shell.IsEditorMode, "The editor is still up under the list.");
            Assert.IsTrue(shell.IsNavTags);
            Assert.IsFalse(shell.IsNavEditor || shell.IsNavTodo || shell.IsNavFavorites || shell.IsNavFiles || shell.IsNavTimeline,
                "More than one sidebar entry is marked.");
            Assert.AreEqual(Daynote.App.Localization.AppStrings.DeskNavTags, shell.ListTitle);
        });
    }

    [TestMethod]
    public void Picking_a_day_keeps_the_list_and_the_editor_brings_it_back()
    {
        WithShell(shell =>
        {
            Wait(shell.ShowListCommand.ExecuteAsync(RightTab.Files));
            Wait(shell.SelectDateAsync(LocalDates.AddDays(shell.SelectedDate, -1)));

            // The files list is per day, so a different day is a reason to stay on it, not to leave.
            Assert.IsTrue(shell.IsListMode, "Changing the day closed the list.");
            Assert.IsTrue(shell.IsNavFiles);

            shell.ShowEditorCommand.Execute(null);
            Assert.IsTrue(shell.IsEditorMode);
            Assert.IsFalse(shell.IsListMode);
        });
    }

    [TestMethod]
    public void A_new_note_opens_in_the_editor_from_any_view()
    {
        WithShell(shell =>
        {
            Wait(shell.ShowListCommand.ExecuteAsync(RightTab.Todo));
            Wait(shell.NewNoteCommand.ExecuteAsync(null));
            Assert.IsTrue(shell.IsEditorMode, "The new note was made behind the list.");

            Wait(shell.ShowTimelineCommand.ExecuteAsync(null));
            Assert.IsTrue(shell.IsTimelineMode);
            Wait(shell.NewNoteCommand.ExecuteAsync(null));
            Assert.IsTrue(shell.IsEditorMode, "The new note was made behind the timeline.");
        });
    }

    [TestMethod]
    public void Opening_the_timeline_from_a_list_leaves_the_list()
    {
        WithShell(shell =>
        {
            Wait(shell.ShowListCommand.ExecuteAsync(RightTab.Favorites));
            Wait(shell.ShowTimelineCommand.ExecuteAsync(null));

            Assert.IsTrue(shell.IsTimelineMode);
            Assert.IsFalse(shell.IsListMode);
            Assert.IsTrue(shell.IsNavTimeline);
        });
    }

    // ── Header ───────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void The_header_spells_out_the_selected_day()
    {
        var localization = Daynote.App.Localization.LocalizationService.Instance;
        Daynote.App.Localization.AppLanguage original = localization.Language;
        try
        {
            WithShell(shell =>
            {
                localization.SetLanguage(Daynote.App.Localization.AppLanguage.Korean);
                Wait(shell.SelectDateAsync(LocalDates.FromDateOnly(new DateOnly(2026, 9, 30))));

                Assert.AreEqual("30", shell.BigDay);
                Assert.AreEqual("9월 · 수요일", shell.BigMeta);
                Assert.AreEqual("2026년 · 노트 0개", shell.BigSub);

                localization.SetLanguage(Daynote.App.Localization.AppLanguage.English);
                Assert.AreEqual("September · Wednesday", shell.BigMeta, "The header did not re-read itself on a language switch.");
            });
        }
        finally
        {
            localization.SetLanguage(original);
        }
    }

    // ── Week strip ───────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void The_week_starts_on_Sunday()
    {
        LocalDate wednesday = LocalDates.FromDateOnly(new DateOnly(2026, 9, 30));
        Assert.AreEqual(LocalDates.FromDateOnly(new DateOnly(2026, 9, 27)), WeekStripViewModel.WeekStart(wednesday));

        LocalDate sunday = LocalDates.FromDateOnly(new DateOnly(2026, 9, 27));
        Assert.AreEqual(sunday, WeekStripViewModel.WeekStart(sunday), "A Sunday starts its own week.");
    }

    [TestMethod]
    public void The_strip_shows_the_selected_week_with_dots_on_days_with_notes()
    {
        WithShell(shell =>
        {
            // A week that straddles two months, so both month summaries are read.
            LocalDate thursday = LocalDates.FromDateOnly(new DateOnly(2026, 10, 1));
            Wait(shell.SelectDateAsync(thursday));
            Wait(shell.NewNoteCommand.ExecuteAsync(null));
            LocalDate tuesday = LocalDates.FromDateOnly(new DateOnly(2026, 9, 29));
            Wait(shell.SelectDateAsync(tuesday));

            WeekStripDayViewModel[] days = [.. shell.Week.Days];
            Assert.HasCount(7, days);
            Assert.AreEqual(LocalDates.FromDateOnly(new DateOnly(2026, 9, 27)), days[0].Date);
            Assert.IsTrue(days[0].IsSunday);
            Assert.IsTrue(days.Single(static d => d.IsSelected).Date == tuesday, "The selected day is not the one marked.");
            Assert.IsTrue(days.Single(d => d.Date == thursday).HasNotes, "The note on the 1st, in the next month, has no dot.");
            Assert.IsFalse(days.Single(d => d.Date == tuesday).HasNotes);
        });
    }

    [TestMethod]
    public void The_arrows_step_a_week_through_the_shell()
    {
        WithShell(shell =>
        {
            LocalDate start = shell.SelectedDate;
            Wait(shell.Week.NextWeekCommand.ExecuteAsync(null));
            Assert.AreEqual(LocalDates.AddDays(start, 7), shell.SelectedDate, "Next week did not move the selected date.");
            Assert.AreEqual(shell.SelectedDate, shell.Calendar.SelectedDate, "The calendar did not follow the strip.");

            Wait(shell.Week.PreviousWeekCommand.ExecuteAsync(null));
            Assert.AreEqual(start, shell.SelectedDate);
        });
    }

    // ── Day panel ────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void The_day_panel_lists_only_the_selected_day_s_todos()
    {
        WithShell(shell =>
        {
            LocalDate today = shell.SelectedDate;
            AddTodo(shell, "오늘 하나");
            AddTodo(shell, "오늘 끝낸 것", done: true);
            Wait(shell.SelectDateAsync(LocalDates.AddDays(today, -1)));
            AddTodo(shell, "어제 것");
            Wait(shell.SelectDateAsync(today));
            Wait(shell.Todo.RefreshAsync());

            // Finished rows moved to their own collapsed list; the day is a record of itself, so
            // they are still here, just not in the way of what is left.
            CollectionAssert.AreEquivalent(
                new[] { "오늘 하나" },
                shell.DayTodos.Select(static t => t.Text).ToArray());
            CollectionAssert.AreEquivalent(
                new[] { "오늘 끝낸 것" },
                shell.DayTodosDone.Select(static t => t.Text).ToArray());
            Assert.IsTrue(shell.HasDayDone);
            Assert.IsFalse(shell.IsDayTodoEmpty);
        });
    }

    // ── Timeline groups ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void The_timeline_folds_into_one_group_per_day()
    {
        static Task Open(Guid id, LocalDate date) => Task.CompletedTask;
        LocalDate first = LocalDates.FromDateOnly(new DateOnly(2026, 9, 30));
        LocalDate second = LocalDates.FromDateOnly(new DateOnly(2026, 9, 28));
        TimelineNoteRow Card(LocalDate date, string title) => new(Guid.NewGuid(), date, title, false, "", "", false, Open);

        TimelineRow[] rows =
        [
            Card(first, "머리 없는 카드"),
            new TimelineDateHeaderRow(first, 2),
            Card(first, "a"),
            Card(first, "b"),
            new TimelineDateHeaderRow(second, 1),
            Card(second, "c"),
        ];

        IReadOnlyList<TimelineDayGroup> groups = TimelineGrouping.Group(rows);

        Assert.HasCount(2, groups);
        Assert.AreEqual(first, groups[0].Date);
        CollectionAssert.AreEqual(new[] { "a", "b" }, groups[0].Notes.Select(static n => n.Title).ToArray());
        CollectionAssert.AreEqual(new[] { "c" }, groups[1].Notes.Select(static n => n.Title).ToArray());
        Assert.IsTrue(groups.All(static g => g.Notes.All(n => n.Title != "머리 없는 카드")), "A card before any header was given a day.");
    }

    [TestMethod]
    public void The_shell_regroups_the_timeline_as_it_loads()
    {
        WithShell(shell =>
        {
            WriteNote(shell, "하나");
            Wait(shell.SelectDateAsync(LocalDates.AddDays(shell.SelectedDate, -2)));
            WriteNote(shell, "둘");
            Wait(shell.ShowTimelineCommand.ExecuteAsync(null));
            Pump();

            Assert.HasCount(2, shell.TimelineGroups);
            Assert.IsTrue(shell.TimelineGroups.All(static g => g.Notes.Count == 1));

            // A day's heading opens that day in the editor.
            TimelineDayGroup older = shell.TimelineGroups[1];
            Wait(shell.OpenTimelineDayCommand.ExecuteAsync(older));
            Assert.IsTrue(shell.IsEditorMode);
            Assert.AreEqual(older.Date, shell.SelectedDate);
        });
    }

    // ── Palette ──────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Closing_the_palette_drops_the_query()
    {
        WithShell(shell =>
        {
            shell.OpenPaletteCommand.Execute(null);
            shell.Search.Query = "회의";
            Assert.IsTrue(shell.Search.IsOpen);

            shell.ClosePaletteCommand.Execute(null);

            Assert.IsFalse(shell.IsPaletteOpen);
            Assert.AreEqual(string.Empty, shell.Search.Query);
            Assert.IsFalse(shell.Search.IsOpen);
        });
    }

    [TestMethod]
    public void The_quick_actions_carry_the_current_chords()
    {
        WithShell(shell =>
        {
            Assert.HasCount(4, shell.QuickActions);
            Assert.AreEqual(shell.NewNoteHint, shell.QuickActions[0].Combo);
            Assert.AreEqual(shell.TodayHint, shell.QuickActions[1].Combo);
            Assert.IsFalse(shell.QuickActions[2].HasCombo, "The timeline has no chord; its row prints none.");
            Assert.AreEqual(shell.SettingsHint, shell.QuickActions[3].Combo);

            shell.OpenPaletteCommand.Execute(null);
            Wait(shell.QuickActions[3].RunCommand.ExecuteAsync(null));
            Assert.IsTrue(shell.IsSettingsOpen);
            Assert.IsFalse(shell.IsPaletteOpen);
        });
    }

    [TestMethod]
    public void A_file_result_opens_the_day_s_files_list()
    {
        WithShell(shell =>
        {
            using (var content = new MemoryStream(new byte[64]))
            {
                Wait(shell.Files.AddFromStreamAsync("회의록.pdf", content));
            }

            Wait(shell.ShowTimelineCommand.ExecuteAsync(null));
            shell.OpenPaletteCommand.Execute(null);
            Wait(shell.Search.SearchNowAsync("회의록"));
            SearchResultRowViewModel file = shell.Search.Results.First(r => r.Kind == Daynote.App.Localization.AppStrings.SearchKindFile);

            Wait(file.ActivateCommand.ExecuteAsync(null));

            Assert.IsFalse(shell.IsPaletteOpen);
            Assert.IsTrue(shell.IsListMode, "A file result should land on the files list.");
            Assert.IsTrue(shell.IsNavFiles);
        });
    }

    // ── Open at login ────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void The_login_switch_follows_the_login_item()
    {
        var gateway = new FakeStartupGateway(StartupTaskState.Disabled);
        WithShell(
            shell =>
            {
                DesktopSettingsViewModel settings = shell.SettingsViewModel!;
                Wait(settings.RefreshAsync());
                Assert.IsFalse(settings.StartupIsOn);
                Assert.IsTrue(settings.StartupToggleEnabled);

                Wait(settings.ToggleStartupCommand.ExecuteAsync(null));
                Assert.AreEqual(1, gateway.Enables, "Turning the switch on never asked for a login item.");
                Assert.IsTrue(settings.StartupIsOn);

                Wait(settings.ToggleStartupCommand.ExecuteAsync(null));
                Assert.AreEqual(1, gateway.Disables);
                Assert.IsFalse(settings.StartupIsOn);
            },
            gateway);
    }

    [TestMethod]
    public void Where_there_is_no_login_item_the_switch_does_nothing()
    {
        var gateway = new FakeStartupGateway(StartupTaskState.Unavailable);
        WithShell(
            shell =>
            {
                DesktopSettingsViewModel settings = shell.SettingsViewModel!;
                Wait(settings.RefreshAsync());
                Assert.IsFalse(settings.StartupToggleEnabled, "The switch is live where nothing can be switched.");

                Wait(settings.ToggleStartupCommand.ExecuteAsync(null));
                Assert.AreEqual(0, gateway.Enables + gateway.Disables, "An unavailable login item was still asked to change.");
                Assert.IsFalse(settings.StartupIsOn);
            },
            gateway);
    }

    // ── Harness ──────────────────────────────────────────────────────────────────────────────

    private sealed class FakeStartupGateway(StartupTaskState initial) : IStartupTaskGateway
    {
        private StartupTaskState _state = initial;

        public int Enables { get; private set; }

        public int Disables { get; private set; }

        public ValueTask<StartupTaskState> GetStateAsync(CancellationToken cancellationToken) => ValueTask.FromResult(_state);

        public ValueTask<StartupTaskState> RequestEnableAsync(CancellationToken cancellationToken)
        {
            Enables++;
            _state = StartupTaskState.Enabled;
            return ValueTask.FromResult(_state);
        }

        public ValueTask<StartupTaskState> DisableAsync(CancellationToken cancellationToken)
        {
            Disables++;
            _state = StartupTaskState.Disabled;
            return ValueTask.FromResult(_state);
        }
    }

    /// <summary>The graph behind the running shell, so a test can seed what the shell reads.</summary>
    private static ServiceProvider? _provider;

    /// <summary>
    /// Stores one to-do on the selected day, the way the @ command would.
    /// </summary>
    /// <remarks>
    /// Written through the repository rather than as a <c>-[]</c> line in a note: the panels read
    /// to-do entities now, and a checkbox in a body is text that looks like one.
    /// </remarks>
    private static void AddTodo(DesktopShellViewModel shell, string title, bool done = false)
    {
        var agenda = _provider!.GetRequiredService<Daynote.Core.Agenda.IAgendaRepository>();
        DateOnly day = LocalDates.ToDateOnly(shell.SelectedDate);
        Wait(agenda.SaveAsync(new Daynote.Core.Agenda.AgendaItem(
            Guid.NewGuid(),
            Daynote.Core.Agenda.AgendaList.DefaultId,
            Daynote.Core.Agenda.AgendaKind.Task,
            title,
            string.Empty,
            "Asia/Seoul",
            StartsAt: null,
            EndsAt: null,
            DueAt: new Daynote.Core.Agenda.WallClock(day.ToDateTime(new TimeOnly(10, 0))),
            HasDueTime: true,
            Rrule: null,
            SeriesId: null,
            RecurrenceId: null,
            done
                ? Daynote.Core.Agenda.AgendaStatus.Completed
                : Daynote.Core.Agenda.AgendaStatus.NeedsAction,
            CompletedUtc: done ? DateTimeOffset.UtcNow : null,
            Priority: 0,
            Daynote.Core.Agenda.TimelineVisibility.Auto,
            SourceNoteId: null,
            ExceptionDates: [],
            Daynote.Core.Agenda.AgendaAlert.Default,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow)).AsTask());
        Pump();
    }

    private static void WriteNote(DesktopShellViewModel shell, string body)
    {
        Wait(shell.NewNoteCommand.ExecuteAsync(null));
        shell.Notes.EditorText = body;
        Wait(shell.Notes.FlushAsync(FlushReason.NoteChange));
        Pump();
    }

    /// <summary>Composes the shell over a throwaway database and initialises it.</summary>
    private static void WithShell(Action<DesktopShellViewModel> body, IStartupTaskGateway? startup = null)
    {
        string dataRoot = Path.Combine(Path.GetTempPath(), "daynote-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);

        HeadlessAppFixture.OnUiThread(() =>
        {
            Environment.SetEnvironmentVariable("DAYNOTE_DATA_ROOT", dataRoot);
            var services = new ServiceCollection();
            services.AddDaynoteDesktop(DaynoteAppOptions.ForCurrentUser(), Application.Current!, () => null, () => { });
            if (startup is not null)
            {
                // Registered last, so it is the one the settings view model is handed.
                services.AddSingleton<IStartupTaskService>(new MsixStartupTaskService(startup));
            }

            ServiceProvider provider = services.BuildServiceProvider();
            _provider = provider;
            var shell = provider.GetRequiredService<DesktopShellViewModel>();
            try
            {
                Wait(shell.InitializeAsync());
                body(shell);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });

        try
        {
            Directory.Delete(dataRoot, recursive: true);
        }
        catch (IOException)
        {
            // A SQLite handle can outlive the test by a moment.
        }
    }

    private static void Wait(Task work)
    {
        for (int i = 0; i < 400 && !work.IsCompleted; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        Assert.IsTrue(work.IsCompleted, "A step never completed.");
        work.GetAwaiter().GetResult();
        Pump();
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(2);
        }
    }
}
