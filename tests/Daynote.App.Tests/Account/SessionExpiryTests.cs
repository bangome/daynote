using Daynote.App.Account;
using Daynote.App.Localization;
using Daynote.Core.Sync;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Account;

/// <summary>
/// A session the server has revoked. Seen from an iPhone in production: the refresh token's family
/// was revoked, every call answered 401, and the app went on showing a signed-in account that could
/// do nothing. A rejected refresh now signs the device out and says why; anything short of that —
/// no network, a server fault — keeps the session.
/// </summary>
/// <remarks>Linked into the Avalonia desktop suite too, which is the one that runs on macOS.</remarks>
[TestClass]
public sealed class SessionExpiryTests
{
    private FakeAccounts accounts = null!;
    private FakeSyncStore store = null!;
    private SyncReport nextReport = null!;
    private Exception? nextSyncFailure;
    private AppLanguage language;

    [TestInitialize]
    public void Setup()
    {
        language = LocalizationService.Instance.Language;
        LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
        store = new FakeSyncStore();
        accounts = new FakeAccounts(store, withTokens: true);
        nextReport = SyncReport.For(SyncOutcome.Completed);
        nextSyncFailure = null;
    }

    [TestCleanup]
    public void Restore() => LocalizationService.Instance.SetLanguage(language);

    private async Task<AccountViewModel> SignedInAsync()
    {
        var vm = new AccountViewModel(
            accounts.Service,
            store,
            () => nextSyncFailure is null
                ? ValueTask.FromResult(nextReport)
                : ValueTask.FromException<SyncReport>(nextSyncFailure),
            new NoExporter(),
            _ => { },
            @"C:\conflicts");
        await vm.SignInCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.IsSignedIn);
        return vm;
    }

    [TestMethod]
    public async Task A_refresh_the_server_rejects_signs_out_and_says_the_session_ended()
    {
        AccountViewModel vm = await SignedInAsync();
        accounts.BillingFailure = new AccountException(AccountFailure.InvalidCredentials, "401");
        accounts.RefreshFailure = new AccountException(AccountFailure.InvalidCredentials, "401");

        await vm.RefreshBillingCommand.ExecuteAsync(null);

        Assert.AreEqual(1, accounts.RefreshCalls, "The 401 was not answered by exactly one refresh.");
        Assert.IsTrue(vm.IsSignedOut, "A revoked session is still shown as signed in.");
        Assert.IsTrue(vm.IsSessionEnded);
        Assert.AreEqual(AppStrings.AccountSessionEnded, vm.NoticeMessage);
        Assert.IsNull(vm.ErrorMessage);
        Assert.IsTrue(vm.ShowsSignIn);
        Assert.IsNull(await accounts.LoadSessionAsync(), "The dead tokens were kept.");
        Assert.IsFalse(accounts.SignedOut, "A logout was sent with a token the server already refused.");
    }

    [TestMethod]
    public async Task The_notice_is_in_both_languages()
    {
        AccountViewModel vm = await SignedInAsync();
        nextReport = SyncReport.For(SyncOutcome.SessionExpired);
        await vm.SyncCommand.ExecuteAsync(null);

        StringAssert.StartsWith(vm.NoticeMessage, "로그인이 만료되었습니다. 다시 로그인해 주세요.");
        LocalizationService.Instance.SetLanguage(AppLanguage.English);
        StringAssert.StartsWith(vm.NoticeMessage, "Your session ended. Please sign in again.");
    }

    [TestMethod]
    public async Task A_sync_that_finds_the_session_revoked_signs_out()
    {
        AccountViewModel vm = await SignedInAsync();

        nextReport = SyncReport.For(SyncOutcome.SessionExpired);
        await vm.SyncCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.IsSignedOut);
        Assert.IsTrue(vm.IsSessionEnded);
        Assert.IsFalse(vm.Status.IsVisible);
    }

    [TestMethod]
    public async Task A_sync_whose_token_cannot_be_renewed_signs_out()
    {
        AccountViewModel vm = await SignedInAsync();

        nextSyncFailure = new AccountException(AccountFailure.SessionExpired, "The session expired.");
        await vm.SyncCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.IsSignedOut);
        Assert.AreEqual(AppStrings.AccountSessionEnded, vm.NoticeMessage);
    }

    [TestMethod]
    [DataRow(AccountFailure.ServerError, DisplayName = "500 on refresh")]
    [DataRow(AccountFailure.Offline, DisplayName = "No network on refresh")]
    public async Task A_refresh_that_merely_failed_keeps_the_session(AccountFailure failure)
    {
        AccountViewModel vm = await SignedInAsync();
        accounts.BillingFailure = new AccountException(AccountFailure.InvalidCredentials, "401");
        accounts.RefreshFailure = new AccountException(failure, "transient");

        await vm.RefreshBillingCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.IsSignedIn, "A transient failure signed the user out.");
        Assert.IsFalse(vm.IsSessionEnded);
        Assert.IsTrue(vm.IsBillingUnavailable);
        Assert.IsNotNull(await accounts.LoadSessionAsync(), "The session was thrown away over a transient failure.");

        // And it recovers by itself once the server answers again.
        accounts.RefreshFailure = null;
        accounts.BillingFailure = null;
        await vm.RefreshBillingCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.IsBillingUnavailable);
    }

    [TestMethod]
    public async Task An_expired_access_token_is_renewed_rather_than_signing_out()
    {
        AccountViewModel vm = await SignedInAsync();
        // The access token alone is stale: one 401, then the refresh succeeds and the retry is good.
        accounts.NextFailure = new AccountException(AccountFailure.InvalidCredentials, "401");

        await vm.RefreshBillingCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.IsSignedIn);
        Assert.AreEqual(1, accounts.RefreshCalls);
        Assert.IsFalse(vm.IsBillingUnavailable);
    }

    [TestMethod]
    public async Task Signing_in_again_clears_the_notice()
    {
        AccountViewModel vm = await SignedInAsync();
        nextReport = SyncReport.For(SyncOutcome.SessionExpired);
        await vm.SyncCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.IsSessionEnded);

        nextReport = SyncReport.For(SyncOutcome.Completed);
        await vm.SignInCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.IsSignedIn);
        Assert.IsFalse(vm.IsSessionEnded);
        Assert.IsNull(vm.NoticeMessage);
    }

    private sealed class NoExporter : IRecoveryKeyExporter
    {
        public Task<bool> TryCopyToClipboardAsync(string recoveryKey) => Task.FromResult(true);

        public Task<bool> TrySaveToFileAsync(string recoveryKey) => Task.FromResult(true);
    }
}
