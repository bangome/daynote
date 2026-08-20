using Daynote.App.Account;
using Daynote.App.Localization;
using Daynote.Core.Sync;

namespace Daynote.App.Tests.Account;

/// <summary>
/// The account surface, driven through fakes. What matters here is what the user is told and when:
/// the recovery key cannot be dismissed unacknowledged, offline is not an error, and no
/// developer-facing English leaks into a Korean UI.
/// </summary>
[TestClass]
public sealed class AccountViewModelTests
{
    private FakeAccounts accounts = null!;
    private FakeStore store = null!;
    private FakeExporter exporter = null!;
    private SyncReport nextReport = null!;
    private Exception? nextSyncFailure;
    private string? revealed;

    [TestInitialize]
    public void Setup()
    {
        store = new FakeStore();
        accounts = new FakeAccounts(store);
        exporter = new FakeExporter();
        nextReport = SyncReport.For(SyncOutcome.Completed);
        nextSyncFailure = null;
        revealed = null;
    }

    private AccountViewModel Create() => new(
        accounts.Service,
        store,
        () => nextSyncFailure is null
            ? ValueTask.FromResult(nextReport)
            : ValueTask.FromException<SyncReport>(nextSyncFailure),
        exporter,
        path => revealed = path,
        @"C:\conflicts");

    [TestMethod]
    public void A_signed_out_account_hides_the_status_chip_entirely()
    {
        // An always-present chip would advertise a feature the user has declined.
        AccountViewModel vm = Create();

        Assert.IsFalse(vm.Status.IsVisible);
        Assert.IsTrue(vm.IsSignedOut);
    }

    [TestMethod]
    public async Task Registering_shows_the_recovery_key_and_blocks_dismissal_until_acknowledged()
    {
        AccountViewModel vm = Create();
        vm.Email = "alice@example.test";

        await vm.RegisterCommand.ExecuteAsync("correct horse battery staple");

        Assert.IsTrue(vm.IsShowingRecoveryKey);
        Assert.AreEqual(32, vm.RecoveryKeyDisplay!.Length);
        // The one time this key is ever visible, so it must not be dismissible by accident.
        Assert.IsFalse(vm.DismissRecoveryKeyCommand.CanExecute(null));

        vm.RecoveryKeyAcknowledged = true;
        Assert.IsTrue(vm.DismissRecoveryKeyCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task Dismissing_the_recovery_key_clears_it_from_memory()
    {
        AccountViewModel vm = Create();
        vm.Email = "alice@example.test";
        await vm.RegisterCommand.ExecuteAsync("correct horse battery staple");
        vm.RecoveryKeyAcknowledged = true;

        await vm.DismissRecoveryKeyCommand.ExecuteAsync(null);

        Assert.IsNull(vm.RecoveryKeyDisplay);
        Assert.IsFalse(vm.IsShowingRecoveryKey);
    }

    [TestMethod]
    public async Task A_wrong_password_shows_localized_copy_and_not_the_exception_text()
    {
        // AccountException messages are developer-facing English. Showing one in a Korean UI is a
        // localization defect, so the view model maps the failure instead of forwarding the message.
        accounts.NextFailure = new AccountException(
            AccountFailure.InvalidCredentials,
            "That email address or password is incorrect.");
        AccountViewModel vm = Create();
        vm.Email = "alice@example.test";

        await vm.SignInCommand.ExecuteAsync("nope");

        Assert.AreEqual(AppStrings.AccountErrorInvalidCredentials, vm.ErrorMessage);
        Assert.IsTrue(vm.IsSignedOut);
    }

    [TestMethod]
    public async Task Every_failure_maps_to_a_message_from_the_catalog()
    {
        foreach (AccountFailure failure in Enum.GetValues<AccountFailure>())
        {
            accounts = new FakeAccounts(store) { NextFailure = new AccountException(failure, "developer text") };
            AccountViewModel vm = Create();
            vm.Email = "alice@example.test";

            await vm.SignInCommand.ExecuteAsync("password12");

            Assert.IsNotNull(vm.ErrorMessage, failure.ToString());
            Assert.AreNotEqual("developer text", vm.ErrorMessage, failure.ToString());
        }
    }

    [TestMethod]
    public async Task A_reset_password_leaves_the_account_locked_rather_than_signed_out()
    {
        accounts.NextFailure = new AccountException(AccountFailure.RewrapRequired, "locked");
        AccountViewModel vm = Create();
        vm.Email = "alice@example.test";

        await vm.SignInCommand.ExecuteAsync("password12");

        // The password was right; only the envelope is stale. Showing the sign-in form again would
        // invite the user to retype a password that already worked.
        Assert.IsTrue(vm.IsLocked);
        Assert.IsTrue(vm.IsSignedIn);
    }

    [TestMethod]
    public async Task Signing_in_syncs_and_reports_synced()
    {
        AccountViewModel vm = Create();
        vm.Email = "alice@example.test";

        await vm.SignInCommand.ExecuteAsync("password12");

        Assert.IsTrue(vm.IsSignedIn);
        Assert.AreEqual(SyncStatusKind.Synced, vm.Status.Kind);
    }

    [TestMethod]
    public async Task Being_offline_is_a_state_not_an_error()
    {
        // A laptop is offline most of the time. Surfacing that as an error message would train the
        // user to ignore error messages.
        nextReport = SyncReport.For(SyncOutcome.Offline);
        AccountViewModel vm = Create();
        vm.Email = "alice@example.test";
        await vm.SignInCommand.ExecuteAsync("password12");

        Assert.AreEqual(SyncStatusKind.Offline, vm.Status.Kind);
        Assert.IsNull(vm.ErrorMessage);
        Assert.IsFalse(vm.Status.NeedsAttention);
    }

    [TestMethod]
    public async Task An_unreadable_record_is_surfaced_rather_than_reported_as_a_clean_sync()
    {
        // Silently skipping it would look exactly like the note having been deleted.
        nextReport = new SyncReport(SyncOutcome.Completed, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 5);
        AccountViewModel vm = Create();
        vm.Email = "alice@example.test";
        await vm.SignInCommand.ExecuteAsync("password12");

        Assert.AreEqual(SyncStatusKind.Error, vm.Status.Kind);
        Assert.IsTrue(vm.Status.NeedsAttention);
    }

    [TestMethod]
    public async Task Replaced_notes_are_announced_with_a_way_to_find_them()
    {
        nextReport = new SyncReport(SyncOutcome.Completed, 0, 0, 0, 1, 1, 0, 0, 0, 0, 2, 5);
        AccountViewModel vm = Create();
        vm.Email = "alice@example.test";
        await vm.SignInCommand.ExecuteAsync("password12");

        Assert.IsTrue(vm.HasReplacedNotes);
        StringAssert.Contains(vm.ReplacedNotesMessage, "2");

        vm.OpenConflictsFolderCommand.Execute(null);
        Assert.AreEqual(@"C:\conflicts", revealed);
        // Dismissed once the user has been shown where to look.
        Assert.IsFalse(vm.HasReplacedNotes);
    }

    [TestMethod]
    public async Task Signing_out_hides_the_chip_and_forgets_the_account()
    {
        AccountViewModel vm = Create();
        vm.Email = "alice@example.test";
        await vm.SignInCommand.ExecuteAsync("password12");

        await vm.SignOutCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.IsSignedOut);
        Assert.IsFalse(vm.Status.IsVisible);
        Assert.IsTrue(accounts.SignedOut);
    }

    [TestMethod]
    public void The_chip_label_follows_the_language()
    {
        AppLanguage original = LocalizationService.Instance.Language;
        try
        {
            var syncing = new SyncStatusView(SyncStatusKind.Syncing);

            LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
            string korean = syncing.Label;
            LocalizationService.Instance.SetLanguage(AppLanguage.English);

            Assert.AreNotEqual(korean, syncing.Label);
            Assert.AreEqual("Syncing", syncing.Label);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(original);
        }
    }

    [TestMethod]
    public void Only_locked_and_error_ask_for_attention()
    {
        foreach (SyncStatusKind kind in Enum.GetValues<SyncStatusKind>())
        {
            bool expected = kind is SyncStatusKind.Locked or SyncStatusKind.Error;
            Assert.AreEqual(expected, new SyncStatusView(kind).NeedsAttention, kind.ToString());
        }
    }

    private sealed class FakeExporter : IRecoveryKeyExporter
    {
        internal string? Copied { get; private set; }

        public bool TryCopyToClipboard(string recoveryKey)
        {
            Copied = recoveryKey;
            return true;
        }

        public bool TrySaveToFile(string recoveryKey) => true;
    }

    private sealed class FakeStore : ISyncStore
    {
        internal SyncStateSnapshot State { get; set; } = new(null, 0, 0, false, null);

        public ValueTask<SyncStateSnapshot> ReadStateAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(State);

        public ValueTask<int> EnrollExistingContentAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);

        public ValueTask<IReadOnlyList<PendingNote>> ReadPendingNotesAsync(int limit, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<PendingNote>>([]);

        public ValueTask<IReadOnlyList<SyncTombstone>> ReadPendingTombstonesAsync(int limit, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SyncTombstone>>([]);

        public ValueTask<int> AcknowledgePushAsync(IReadOnlyList<PendingAck> acknowledged, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);

        public ValueTask<int> AcknowledgeTombstonesAsync(IReadOnlyList<SyncTombstone> acknowledged, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);

        public ValueTask<MergeOutcome> MergeNotesAsync(IReadOnlyList<SyncNote> notes, IReadOnlyList<SyncTombstone> tombstones, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(MergeOutcome.Empty);

        public ValueTask AdvanceCursorAsync(long cursor, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask SignInAsync(string userId, int dekGeneration, CancellationToken cancellationToken = default)
        {
            State = State with { UserId = userId, DekGeneration = dekGeneration };
            return ValueTask.CompletedTask;
        }

        public ValueTask SignOutAsync(CancellationToken cancellationToken = default)
        {
            State = new SyncStateSnapshot(null, 0, 0, false, null);
            return ValueTask.CompletedTask;
        }

        public ValueTask SetLockedAsync(bool locked, CancellationToken cancellationToken = default)
        {
            State = State with { IsLocked = locked };
            return ValueTask.CompletedTask;
        }
    }
}
