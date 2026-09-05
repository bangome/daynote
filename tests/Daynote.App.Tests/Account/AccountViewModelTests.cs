using Daynote.App.Account;
using Daynote.App.Localization;
using Daynote.Core.Sync;

namespace Daynote.App.Tests.Account;

/// <summary>
/// The account surface, driven through fakes. What matters here is what the user is told and when:
/// closing the browser is not an error, offline is not an error, and no developer-facing English
/// leaks into a Korean UI.
/// </summary>
[TestClass]
public sealed class AccountViewModelTests
{
    private FakeAccounts accounts = null!;
    private FakeSyncStore store = null!;
    private FakeExporter exporter = null!;
    private SyncReport nextReport = null!;
    private Exception? nextSyncFailure;
    private string? revealed;

    [TestInitialize]
    public void Setup()
    {
        store = new FakeSyncStore();
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
    public async Task Signing_in_names_the_account_and_reports_synced()
    {
        AccountViewModel vm = Create();

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.IsSignedIn);
        Assert.AreEqual("alice@example.test", vm.SignedInEmail);
        Assert.AreEqual(SyncStatusKind.Synced, vm.Status.Kind);
    }

    [TestMethod]
    public async Task Closing_the_browser_says_so_quietly_and_leaves_the_account_signed_out()
    {
        // Cancelling is a decision, not a fault. Reporting "the sync service had a problem" here
        // would send the user looking for a problem that does not exist.
        accounts.NextIdentityFailure = new AccountException(
            AccountFailure.SignInCancelled,
            "developer-facing English");
        AccountViewModel vm = Create();

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.IsSignedOut);
        Assert.AreEqual(AppStrings.AccountErrorSignInCancelled, vm.ErrorMessage);
        Assert.AreEqual(0, accounts.SignInCalls);
    }

    [TestMethod]
    public async Task A_failure_shows_localized_copy_and_not_the_exception_text()
    {
        accounts.NextFailure = new AccountException(
            AccountFailure.InvalidCredentials,
            "That sign-in is no longer valid. Sign in again.");
        AccountViewModel vm = Create();

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.AreEqual(AppStrings.AccountErrorInvalidCredentials, vm.ErrorMessage);
        Assert.IsTrue(vm.IsSignedOut);
    }

    [TestMethod]
    public async Task Every_failure_maps_to_a_message_from_the_catalog()
    {
        // A failure with no mapping would render as an untranslated exception message, which is a
        // defect in a bilingual app rather than a cosmetic problem.
        foreach (AccountFailure failure in Enum.GetValues<AccountFailure>())
        {
            accounts.NextFailure = new AccountException(failure, "developer-facing English");
            AccountViewModel vm = Create();

            await vm.SignInCommand.ExecuteAsync(null);

            Assert.IsNotNull(vm.ErrorMessage, failure.ToString());
            Assert.AreNotEqual("developer-facing English", vm.ErrorMessage, failure.ToString());
        }
    }

    [TestMethod]
    public async Task A_session_without_its_key_offers_to_fetch_it_again()
    {
        accounts.WithholdDataKey = true;
        AccountViewModel vm = Create();

        // The server answered without a key, so signing in fails rather than half-succeeding.
        await vm.SignInCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.IsSignedOut);
        Assert.AreEqual(AppStrings.AccountErrorServer, vm.ErrorMessage);

        // With the key back, the ordinary path works and the prompt is gone.
        accounts.WithholdDataKey = false;
        await vm.SignInCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.IsSignedIn);
        Assert.IsFalse(vm.IsKeyMissing);
    }

    [TestMethod]
    public async Task Fetching_the_key_again_clears_the_locked_state()
    {
        AccountViewModel vm = Create();
        await vm.SignInCommand.ExecuteAsync(null);

        nextReport = SyncReport.For(SyncOutcome.Locked);
        await vm.SyncCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.IsKeyMissing);
        Assert.AreEqual(SyncStatusKind.Locked, vm.Status.Kind);

        nextReport = SyncReport.For(SyncOutcome.Completed);
        await vm.RestoreKeyCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.IsKeyMissing);
        Assert.IsFalse(store.State.IsLocked);
        Assert.AreEqual(SyncStatusKind.Synced, vm.Status.Kind);
    }

    [TestMethod]
    public async Task Being_offline_is_a_state_not_an_error()
    {
        // A laptop is offline most of the time. Surfacing that as an error message would train the
        // user to ignore error messages.
        nextReport = SyncReport.For(SyncOutcome.Offline);
        AccountViewModel vm = Create();
        await vm.SignInCommand.ExecuteAsync(null);

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
        await vm.SignInCommand.ExecuteAsync(null);

        Assert.AreEqual(SyncStatusKind.Error, vm.Status.Kind);
        Assert.IsTrue(vm.Status.NeedsAttention);
    }

    [TestMethod]
    public async Task Replaced_notes_are_announced_with_a_way_to_find_them()
    {
        nextReport = new SyncReport(SyncOutcome.Completed, 0, 0, 0, 1, 1, 0, 0, 0, 0, 2, 5);
        AccountViewModel vm = Create();
        await vm.SignInCommand.ExecuteAsync(null);

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
        await vm.SignInCommand.ExecuteAsync(null);

        await vm.SignOutCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.IsSignedOut);
        Assert.IsFalse(vm.Status.IsVisible);
        Assert.IsTrue(accounts.SignedOut);
        Assert.IsNull(await accounts.LoadSessionAsync());
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

    internal sealed class FakeExporter : IRecoveryKeyExporter
    {
        internal string? Copied { get; private set; }

        public Task<bool> TryCopyToClipboardAsync(string recoveryKey)
        {
            Copied = recoveryKey;
            return Task.FromResult(true);
        }

        public Task<bool> TrySaveToFileAsync(string recoveryKey) => Task.FromResult(true);
    }

    [TestMethod]
    public void Only_the_states_the_user_can_act_on_ask_for_attention()
    {
        // Locked needs a passphrase, Error needs looking at, and Unpaid needs a subscription. The
        // rest — synced, syncing, pending, offline — are states to read, not problems to solve.
        foreach (SyncStatusKind kind in Enum.GetValues<SyncStatusKind>())
        {
            bool expected = kind is SyncStatusKind.Locked or SyncStatusKind.Error or SyncStatusKind.Unpaid;
            Assert.AreEqual(expected, new SyncStatusView(kind).NeedsAttention, kind.ToString());
        }
    }
}
