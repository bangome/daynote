using Daynote.App.Account;
using Daynote.App.Localization;
using Daynote.Core.Sync;

namespace Daynote.App.Tests.Account;

/// <summary>
/// The reset and unlock surface. The thing worth protecting here is that the user is told what a
/// reset costs *before* they do it, and that a locked account keeps a visible route out.
/// </summary>
[TestClass]
public sealed class AccountRecoveryViewModelTests
{
    private FakeAccounts accounts = null!;
    private FakeSyncStore store = null!;
    private SyncReport nextReport = null!;

    [TestInitialize]
    public void Setup()
    {
        store = new FakeSyncStore();
        accounts = new FakeAccounts(store);
        nextReport = SyncReport.For(SyncOutcome.Completed);
    }

    private AccountViewModel Create() => new(
        accounts.Service,
        store,
        () => ValueTask.FromResult(nextReport),
        new NoopExporter(),
        _ => { },
        @"C:\conflicts");

    [TestMethod]
    public void Starting_a_reset_replaces_the_sign_in_form()
    {
        AccountViewModel vm = Create();

        vm.BeginResetCommand.Execute(null);

        Assert.IsTrue(vm.IsResetting);
        // Otherwise both forms would be on screen at once, each with its own password box.
        Assert.IsFalse(vm.IsSignedOut);
    }

    [TestMethod]
    public async Task Requesting_a_code_reports_the_same_thing_for_an_unregistered_address()
    {
        // The server refuses to say whether an address exists; the UI must not undo that by showing a
        // different message for the two cases.
        AccountViewModel vm = Create();
        vm.Email = "nobody@example.test";
        vm.BeginResetCommand.Execute(null);

        await vm.RequestResetCodeCommand.ExecuteAsync(null);

        Assert.AreEqual("nobody@example.test", vm.ResetSentTo);
        StringAssert.Contains(vm.ResetSentMessage, "nobody@example.test");
        Assert.IsNull(vm.ErrorMessage);
    }

    [TestMethod]
    public async Task A_reset_on_a_device_that_still_has_the_key_needs_no_recovery_key()
    {
        // §4.8b: the common case. Asking for a recovery key here would be a needless demand.
        AccountViewModel vm = Create();
        vm.Email = "alice@example.test";
        await vm.SignInCommand.ExecuteAsync("the old password");
        vm.BeginResetCommand.Execute(null);
        vm.ResetCode = "ABCD-2345";

        await vm.ConfirmResetCommand.ExecuteAsync("the new password");

        Assert.IsFalse(vm.IsResetting);
        Assert.IsFalse(vm.IsUnlocking);
        Assert.IsFalse(vm.IsLocked);
        Assert.IsTrue(vm.IsSignedIn);
    }

    [TestMethod]
    public async Task A_reset_on_a_fresh_device_offers_the_unlock_form()
    {
        // Locked with no way forward would be the worst outcome; the form is the way forward.
        accounts.LockAfterReset = true;
        AccountViewModel vm = Create();
        vm.Email = "alice@example.test";
        vm.BeginResetCommand.Execute(null);
        vm.ResetCode = "ABCD-2345";

        await vm.ConfirmResetCommand.ExecuteAsync("the new password");

        Assert.IsTrue(vm.IsLocked);
        Assert.IsTrue(vm.IsUnlocking);
        Assert.AreEqual(SyncStatusKind.Locked, new SyncStatusView(SyncStatusKind.Locked).Kind);
    }

    [TestMethod]
    public async Task A_wrong_reset_code_says_so_without_ending_the_reset()
    {
        accounts.NextFailure = new AccountException(AccountFailure.InvalidResetCode, "developer text");
        AccountViewModel vm = Create();
        vm.Email = "alice@example.test";
        vm.BeginResetCommand.Execute(null);
        vm.ResetCode = "ZZZZ-ZZZZ";

        await vm.ConfirmResetCommand.ExecuteAsync("the new password");

        Assert.AreEqual(AppStrings.AccountErrorInvalidResetCode, vm.ErrorMessage);
        // Still on the form, so the user can try the code again rather than starting over.
        Assert.IsTrue(vm.IsResetting);
    }

    [TestMethod]
    public async Task A_malformed_recovery_key_is_rejected_without_a_round_trip()
    {
        AccountViewModel vm = Create();
        vm.Email = "alice@example.test";
        await vm.SignInCommand.ExecuteAsync("the old password");
        vm.RecoveryKeyEntry = "not a recovery key";

        await vm.UnlockCommand.ExecuteAsync("the new password");

        Assert.AreEqual(AppStrings.AccountErrorInvalidRecoveryKeyEntered, vm.ErrorMessage);
        // A typo should not cost a network call, nor be reported by the server as a mismatch.
        Assert.AreEqual(0, accounts.UnlockAttempts);
    }

    [TestMethod]
    public async Task A_well_formed_recovery_key_is_sent_for_unlocking()
    {
        AccountViewModel vm = Create();
        vm.Email = "alice@example.test";
        await vm.SignInCommand.ExecuteAsync("the old password");
        vm.RecoveryKeyEntry = RecoveryKey.Generate().ToDisplayString();

        await vm.UnlockCommand.ExecuteAsync("the new password");

        Assert.AreEqual(1, accounts.UnlockAttempts);
        Assert.IsFalse(vm.IsUnlocking);
        Assert.IsFalse(vm.IsLocked);
        Assert.AreEqual(string.Empty, vm.RecoveryKeyEntry);
    }

    [TestMethod]
    public async Task Discarding_the_cloud_copy_signs_out_and_hides_the_chip()
    {
        AccountViewModel vm = Create();
        vm.Email = "alice@example.test";
        await vm.SignInCommand.ExecuteAsync("the old password");

        await vm.DiscardCloudCopyCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.IsSignedOut);
        Assert.IsFalse(vm.Status.IsVisible);
        Assert.IsFalse(vm.IsUnlocking);
    }

    [TestMethod]
    public async Task Every_recovery_failure_maps_to_its_own_catalog_copy()
    {
        // This used to assert only that the message was not the developer text, which the generic
        // "the sync service had a problem" satisfied — so it passed while all three of these
        // failures rendered identically and told the user to wait for nothing.
        (AccountFailure Failure, string Expected)[] cases =
        [
            (AccountFailure.InvalidResetCode, AppStrings.AccountErrorInvalidResetCode),
            (AccountFailure.InvalidRecoveryKey, AppStrings.AccountErrorInvalidRecoveryKeyEntered),
            (AccountFailure.NoWayToUnlock, AppStrings.AccountErrorNoWayToUnlock),
        ];

        foreach ((AccountFailure failure, string expected) in cases)
        {
            accounts = new FakeAccounts(store) { NextFailure = new AccountException(failure, "developer text") };
            AccountViewModel vm = Create();
            vm.Email = "alice@example.test";
            vm.BeginResetCommand.Execute(null);
            vm.ResetCode = "ABCD-2345";

            await vm.ConfirmResetCommand.ExecuteAsync("the new password");

            Assert.AreEqual(expected, vm.ErrorMessage, failure.ToString());
            Assert.AreNotEqual(AppStrings.AccountErrorServer, vm.ErrorMessage, failure.ToString());
        }
    }

    [TestMethod]
    public async Task An_account_with_no_recovery_envelope_says_so_instead_of_blaming_the_server()
    {
        accounts = new FakeAccounts(store) { RecoveryEnvelopeStored = false };
        AccountViewModel vm = Create();
        vm.Email = "alice@example.test";
        await vm.SignInCommand.ExecuteAsync("the old password");
        vm.RecoveryKeyEntry = RecoveryKey.Generate().ToDisplayString();

        await vm.UnlockCommand.ExecuteAsync("the new password");

        // Nothing on this PC can open the cloud copy, so the user needs the other-device / discard
        // options named — not an invitation to try again later.
        Assert.AreEqual(AppStrings.AccountErrorNoWayToUnlock, vm.ErrorMessage);
        Assert.AreEqual(0, accounts.UnlockAttempts);
    }

    [TestMethod]
    public void Cancelling_a_reset_clears_the_code_it_had_collected()
    {
        AccountViewModel vm = Create();
        vm.BeginResetCommand.Execute(null);
        vm.ResetCode = "ABCD-2345";

        vm.CancelResetCommand.Execute(null);

        Assert.IsFalse(vm.IsResetting);
        Assert.AreEqual(string.Empty, vm.ResetCode);
        Assert.IsTrue(vm.IsSignedOut);
    }

    private sealed class NoopExporter : IRecoveryKeyExporter
    {
        public bool TryCopyToClipboard(string recoveryKey) => true;

        public bool TrySaveToFile(string recoveryKey) => true;
    }
}
