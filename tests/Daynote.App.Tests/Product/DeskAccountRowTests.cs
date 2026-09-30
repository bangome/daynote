using Daynote.App.Account;
using Daynote.App.Localization;
using Daynote.App.Settings;
using Daynote.App.Shell.Product;
using Daynote.App.Tests.Account;
using Daynote.App.Tests.Lifecycle;
using Daynote.App.Tests.Workspace;
using Daynote.Core.Startup;
using Daynote.Core.Sync;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Product;

/// <summary>
/// The sidebar's account row says who is signed in, and changes when that changes.
/// </summary>
/// <remarks>
/// Every line of the row is derived: the shell's Account comes off the settings view model, and the
/// row's title, subtitle and letter come off that. Nothing in the chain raises a change by itself,
/// so the row was written once and kept saying "sign in" through a sign-in. Signing in is the one
/// state change here that a person sees immediately and would not think to re-check.
/// </remarks>
[TestClass]
public sealed class DeskAccountRowTests
{
    private FakeAccounts accounts = null!;

    [TestInitialize]
    public void Setup() => accounts = new FakeAccounts();

    [TestMethod]
    public async Task Signing_in_renames_the_row()
    {
        accounts.Email = "jiwon@example.test";
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await using (harness)
        {
            AccountViewModel account = Account();
            harness.Shell.SettingsViewModel = Settings(account);

            string signedOut = harness.Shell.AccountCardTitle;
            Assert.AreEqual(AppStrings.AccountBarSignIn, signedOut, "A shell with an account but no session should offer one.");

            var raised = new List<string>();
            harness.Shell.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

            await account.SignInCommand.ExecuteAsync(null);

            StringAssert.Contains(
                harness.Shell.AccountCardTitle,
                "jiwon@example.test",
                $"The row still reads '{harness.Shell.AccountCardTitle}' after signing in.");
            Assert.AreEqual("J", harness.Shell.AccountInitial, "The avatar kept the product's letter.");
            CollectionAssert.Contains(raised, nameof(ProductShellViewModel.AccountCardTitle), "Nothing told the sidebar the row changed.");
        }
    }

    [TestMethod]
    public async Task The_row_is_right_the_moment_composition_hands_over_the_account()
    {
        accounts.Email = "jiwon@example.test";
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await using (harness)
        {
            AccountViewModel account = Account();
            await account.SignInCommand.ExecuteAsync(null);

            // Composition signs in before it hands the settings view model to the shell on a restart
            // with a stored session, so the row has to be right on arrival, not only on a change.
            harness.Shell.SettingsViewModel = Settings(account);

            StringAssert.Contains(harness.Shell.AccountCardTitle, "jiwon@example.test");
        }
    }

    [TestMethod]
    public async Task Without_a_cloud_endpoint_the_row_says_where_the_notes_live()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await using (harness)
        {
            harness.Shell.SettingsViewModel = Settings(account: null);

            Assert.IsNull(harness.Shell.Account);
            Assert.AreEqual(AppStrings.AccountBarLocal, harness.Shell.AccountCardTitle);
        }
    }

    private AccountViewModel Account() => new(
        accounts.Service,
        accounts.Store,
        () => ValueTask.FromResult(SyncReport.For(SyncOutcome.Completed)),
        new AccountViewModelTests.FakeExporter(),
        _ => { },
        @"C:\conflicts");

    private static SettingsViewModel Settings(AccountViewModel? account)
    {
        var store = new InMemorySettingsStore();
        return new SettingsViewModel(
            new FakeStartupTaskService(StartupTaskState.Disabled),
            new RecordingHotkeyService(),
            store,
            new FakeBackupService(),
            new FakeBackupFilePicker(),
            new Daynote.App.Input.ConfigurableShortcuts(store),
            () => Task.FromResult(true),
            () => { },
            () => { },
            @"C:\Users\Test\AppData\Local\Daynote")
        {
            Account = account,
        };
    }
}
