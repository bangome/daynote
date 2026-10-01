using System.Reflection;
using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Threading;
using Daynote.App.Account;
using Daynote.App.Composition;
using Daynote.App.Notes;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;
using Daynote.Core.Sync;
using Daynote.Desktop.Composition;
using Daynote.Desktop.ViewModels;
using Daynote.Infrastructure.Notes;
using Daynote.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Desktop.Tests;

/// <summary>
/// What automatic sync does to the screen. The pull is faked by writing straight to the database,
/// which is what the real engine does underneath the views; everything above it is the real shell.
/// </summary>
[TestClass]
public sealed class AutoSyncRefreshTests
{
    [TestMethod]
    public void A_note_pulled_by_sync_appears_without_reselecting_the_date() => WithSignedInShell((shell, pull) =>
    {
        pull.Next = "PULLED-FROM-PHONE";

        shell.StartAutoSync();

        PumpUntil(() => shell.Notes.Tabs.Any(tab => tab.Body == "PULLED-FROM-PHONE"), "the pulled note never reached the day list");
        Assert.AreEqual(1, pull.Runs, "Starting the shell is one sync, not more.");
    });

    [TestMethod]
    public void A_pull_never_replaces_text_the_user_has_not_saved() => WithSignedInShell((shell, pull) =>
    {
        shell.StartAutoSync();
        PumpUntil(() => pull.Runs == 1, "the start-up sync did not run");
        Assert.IsTrue(shell.NewNoteCommand.CanExecute(null));
        shell.NewNoteCommand.Execute(null);
        PumpUntil(() => shell.Notes.SelectedTab is { IsProjection: false }, "no note to type into");

        shell.Notes.EditorText = "typed, not yet saved";
        Assert.IsTrue(shell.Notes.HasPendingEdits);
        pull.Next = "PULLED-WHILE-TYPING";
        Task manual = shell.Account!.SyncCommand.ExecuteAsync(null);
        PumpUntil(() => manual.IsCompleted, "the manual sync did not finish");
        manual.GetAwaiter().GetResult();

        Assert.AreEqual("typed, not yet saved", shell.Notes.EditorText, "The pull overwrote the buffer.");

        // Once the typing is saved, the note the pull brought in shows up after all.
        PumpUntil(() => shell.Notes.Tabs.Any(tab => tab.Body == "PULLED-WHILE-TYPING"), "the owed refresh never ran");
        Assert.AreEqual("typed, not yet saved", shell.Notes.EditorText);
    });

    [TestMethod]
    public void A_sync_that_changed_nothing_leaves_the_views_alone() => WithSignedInShell((shell, pull) =>
    {
        int tabsBefore = shell.Notes.Tabs.Count;
        NoteTabViewModel? selectedBefore = shell.Notes.SelectedTab;

        shell.StartAutoSync();
        PumpUntil(() => pull.Runs == 1, "the start-up sync did not run");
        Pump();

        Assert.AreEqual(tabsBefore, shell.Notes.Tabs.Count);
        Assert.AreSame(selectedBefore, shell.Notes.SelectedTab, "An empty run rebuilt the tabs anyway.");
    });

    [TestMethod]
    public void A_reload_overtaken_by_navigation_does_not_put_the_old_day_back() => WithSignedInShell((shell, pull) =>
    {
        pull.Next = "ON-THE-FIRST-DAY";
        shell.StartAutoSync();
        PumpUntil(() => shell.Notes.Tabs.Any(tab => tab.Body == "ON-THE-FIRST-DAY"), "the note never arrived");
        LocalDate firstDay = shell.SelectedDate;
        LocalDate nextDay = LocalDates.FromDateOnly(new DateOnly(firstDay.Year, firstDay.Month, firstDay.Day).AddDays(1));

        // The reload reads the first day; before its read lands, the user moves on.
        TaskCompletionSource readLands = GatedRepository.HoldNextRead();
        Task<SyncReloadResult> reload = shell.Notes.ReloadAfterSyncAsync();
        Task<bool> navigation = shell.SelectDateAsync(nextDay);
        PumpUntil(() => navigation.IsCompleted, "the navigation did not finish");
        readLands.SetResult();
        PumpUntil(() => reload.IsCompleted, "the reload did not finish");

        Assert.AreEqual(SyncReloadResult.Skipped, reload.Result);
        Assert.AreEqual(nextDay, shell.Notes.SelectedDate);
        Assert.IsTrue(
            shell.Notes.Tabs.All(tab => tab.Body != "ON-THE-FIRST-DAY"),
            "The first day's notes were rebuilt under the second day's heading.");
    });

    [TestMethod]
    public void Typing_that_autosaves_reads_as_saved() => WithSignedInShell((shell, _) =>
    {
        shell.NewNoteCommand.Execute(null);
        PumpUntil(() => shell.Notes.SelectedTab is { IsProjection: false }, "no note to type into");

        shell.Notes.EditorText = "a pause long enough for the debounce";

        // Before the fix only switching note or date reported the save, so the header said
        // "Unsaved" over text already on disk and automatic sync waited for ever.
        PumpUntil(() => shell.Notes.SaveStatus == SaveStatusKind.Saved, "the debounced save was never reported");
        Assert.IsFalse(shell.Notes.HasPendingEdits);
    });

    [TestMethod]
    public void A_note_deleted_on_another_device_leaves_the_day_list() => WithSignedInShell((shell, pull) =>
    {
        pull.Next = "SOON-DELETED";
        shell.StartAutoSync();
        PumpUntil(() => shell.Notes.Tabs.Any(tab => tab.Body == "SOON-DELETED"), "the note never arrived");
        NoteTabViewModel doomed = shell.Notes.Tabs.First(tab => tab.Body == "SOON-DELETED");
        Task open = shell.Notes.SelectNoteAsync(doomed);
        PumpUntil(() => open.IsCompleted, "could not open the note");

        pull.DeleteNext = doomed.Id;
        Task manual = shell.Account!.SyncCommand.ExecuteAsync(null);
        PumpUntil(() => manual.IsCompleted, "the manual sync did not finish");
        manual.GetAwaiter().GetResult();

        PumpUntil(() => shell.Notes.Tabs.All(tab => tab.Body != "SOON-DELETED"), "the deleted note stayed in the list");
        Assert.AreNotEqual(doomed.Id, shell.Notes.SelectedTab?.Id, "The editor is still on a note that no longer exists.");
    });

    /// <summary>
    /// The real shell and data root, signed in to an account whose "server" is <see cref="FakePull"/>.
    /// </summary>
    private static void WithSignedInShell(Action<DesktopShellViewModel, FakePull> body)
    {
        using var data = new TempDataRoot();

        // Sync has to be registered, whatever an earlier test in this process left the variable at.
        string? previousEndpoint = Environment.GetEnvironmentVariable("DAYNOTE_SYNC_ENDPOINT");
        Environment.SetEnvironmentVariable("DAYNOTE_SYNC_ENDPOINT", "https://example.invalid/");
        try
        {
            RunSignedInShell(data, body);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DAYNOTE_SYNC_ENDPOINT", previousEndpoint);
        }
    }

    private static void RunSignedInShell(TempDataRoot data, Action<DesktopShellViewModel, FakePull> body)
    {
        HeadlessAppFixture.OnUiThread(() =>
        {
            Environment.SetEnvironmentVariable("DAYNOTE_DATA_ROOT", data.Path);
            var services = new ServiceCollection();
            services.AddDaynoteDesktop(DaynoteAppOptions.ForCurrentUser(), Application.Current!, () => null, () => { });
            services.AddSingleton<INoteRepository>(sp => GatedRepository.Wrap(
                new SqliteNoteRepository(sp.GetRequiredService<SqliteDatabase>())));
            ServiceProvider provider = services.BuildServiceProvider();
            var shell = provider.GetRequiredService<DesktopShellViewModel>();
            Task initialising = shell.InitializeAsync();
            PumpUntil(() => initialising.IsCompleted, "the shell did not initialise");
            initialising.GetAwaiter().GetResult();

            var pull = new FakePull(provider.GetRequiredService<ISyncStore>(), shell.SelectedDate);
            var account = new AccountViewModel(
                provider.GetRequiredService<AccountService>(),
                provider.GetRequiredService<ISyncStore>(),
                pull.RunAsync,
                new NoExport(),
                _ => { },
                Path.Combine(data.Path, "conflicts"))
            {
                SignedInEmail = "someone@example.com",
            };
            shell.Account = account;

            try
            {
                body(shell, pull);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    private static void PumpUntil(Func<bool> condition, string failure)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, $"Timed out: {failure}.");
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }

    private static void Pump()
    {
        for (int i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }

    /// <summary>
    /// One sync run that writes the way a real pull does, through the sync store's merge: a note
    /// with <see cref="Next"/> as its body, or a delete of <see cref="DeleteNext"/>.
    /// </summary>
    private sealed class FakePull(ISyncStore store, LocalDate date)
    {
        private DateTimeOffset stamp = DateTimeOffset.UtcNow.AddMinutes(1);

        public string? Next { get; set; }

        public NoteId? DeleteNext { get; set; }

        public int Runs { get; private set; }

        public async ValueTask<SyncReport> RunAsync()
        {
            Runs++;
            SyncNote[] notes = Next is { } body
                ? [new SyncNote(Guid.NewGuid().ToString("D"), date, "From another device", body, 99, false, true, [], stamp, stamp)]
                : [];
            SyncTombstone[] deletes = DeleteNext is { } id ? [new SyncTombstone(SyncEntityKind.Note, id.ToString(), stamp)] : [];
            Next = null;
            DeleteNext = null;
            stamp = stamp.AddSeconds(1);

            MergeOutcome merged = await store.MergeNotesAsync(notes, deletes);
            return SyncReport.For(SyncOutcome.Completed) with
            {
                Pulled = notes.Length + deletes.Length,
                Applied = merged.Applied,
                Deleted = merged.Deleted,
            };
        }
    }

    /// <summary>
    /// The real repository, except that one day read can be held until the test lets it land — the
    /// only way to make "the read came back after the user navigated" happen on demand.
    /// </summary>
    public class GatedRepository : DispatchProxy
    {
        private static TaskCompletionSource? held;
        private INoteRepository inner = null!;

        public static INoteRepository Wrap(INoteRepository inner)
        {
            INoteRepository proxy = Create<INoteRepository, GatedRepository>();
            ((GatedRepository)(object)proxy).inner = inner;
            return proxy;
        }

        public static TaskCompletionSource HoldNextRead() => held = new TaskCompletionSource();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            if (targetMethod.Name == nameof(INoteRepository.GetDayWorkspaceStateAsync) && held is { } gate)
            {
                held = null;
                return new ValueTask<DayWorkspace>(ReadWhenReleasedAsync(gate, (LocalDate)args![0]!, (CancellationToken)args[1]!));
            }

            try
            {
                return targetMethod.Invoke(inner, args);
            }
            catch (TargetInvocationException wrapped) when (wrapped.InnerException is { } actual)
            {
                ExceptionDispatchInfo.Throw(actual);
                throw;
            }
        }

        private async Task<DayWorkspace> ReadWhenReleasedAsync(TaskCompletionSource gate, LocalDate date, CancellationToken token)
        {
            await gate.Task.ConfigureAwait(true);
            return await inner.GetDayWorkspaceStateAsync(date, token).ConfigureAwait(true);
        }
    }

    private sealed class NoExport : IRecoveryKeyExporter
    {
        public Task<bool> TryCopyToClipboardAsync(string recoveryKey) => Task.FromResult(false);

        public Task<bool> TrySaveToFileAsync(string recoveryKey) => Task.FromResult(false);
    }
}
