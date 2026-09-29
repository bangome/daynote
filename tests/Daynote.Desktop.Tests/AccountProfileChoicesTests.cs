using Avalonia;
using Avalonia.Controls;
using Avalonia.Logging;
using Avalonia.LogicalTree;
using Daynote.App.Account;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.Desktop.Composition;
using Daynote.Desktop.ViewModels;
using Daynote.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Desktop.Tests;

/// <summary>
/// The account card's questions about this device's notes (docs/PROFILES.md §5): the Move/Keep
/// question after a sign-in, the sign-out choice with its second confirmation, and a deleted
/// account's notes. Each is driven by setting the view model's state directly — the flows behind
/// them are covered in the portable suite — so what this checks is that each renders, hides what it
/// replaces, and binds its buttons to the right commands without a binding error.
/// </summary>
[TestClass]
public sealed class AccountProfileChoicesTests
{
    [TestMethod]
    public void The_move_question_replaces_the_sign_in_button() => WithAccountPanel((panel, account) =>
    {
        Assert.IsTrue(Visible(panel, AppStrings.AccountSignInWithGoogle));

        account.IsChoosingHandOff = true;
        panel.UpdateLayout();

        Assert.AreSame(account.MoveLocalNotesCommand, VisibleButton(panel, AppStrings.ProfileMoveConfirm).Command);
        Assert.AreSame(account.KeepLocalNotesCommand, VisibleButton(panel, AppStrings.ProfileMoveKeep).Command);
        Assert.IsTrue(TextShown(panel, account.HandOffMessage), "The question does not say what would move.");
        Assert.IsFalse(Visible(panel, AppStrings.AccountSignInWithGoogle), "Sign-in stayed up beside the question.");
    });

    [TestMethod]
    public void The_sign_out_choice_asks_twice_before_removing_unsynced_changes() => WithAccountPanel((panel, account) =>
    {
        account.SignedInEmail = "someone@example.com";
        account.UnsyncedChangeCount = 3;
        account.IsChoosingSignOut = true;
        panel.UpdateLayout();

        Assert.AreSame(account.SignOutKeepCommand, VisibleButton(panel, AppStrings.SignOutKeep).Command);
        Assert.AreSame(account.SignOutRemoveCommand, VisibleButton(panel, AppStrings.SignOutRemove).Command);
        Assert.AreSame(account.CancelSignOutCommand, VisibleButton(panel, AppStrings.SignOutCancel).Command);
        Assert.IsTrue(TextShown(panel, account.UnsyncedChangesMessage), "The unsynced changes are not mentioned.");
        Assert.IsFalse(Visible(panel, AppStrings.SignOutRemoveConfirm));

        account.IsConfirmingRemoveUnsynced = true;
        panel.UpdateLayout();

        Assert.AreSame(account.SignOutRemoveCommand, VisibleButton(panel, AppStrings.SignOutRemoveConfirm).Command);
        Assert.IsTrue(TextShown(panel, account.RemoveUnsyncedMessage));
        Assert.IsFalse(Visible(panel, AppStrings.SignOutRemove), "The first Remove is still armed beside the confirmation.");
    });

    [TestMethod]
    public void A_deleted_accounts_notes_are_asked_about() => WithAccountPanel((panel, account) =>
    {
        account.IsChoosingDeletedNotes = true;
        panel.UpdateLayout();

        Assert.AreSame(account.KeepDeletedNotesCommand, VisibleButton(panel, AppStrings.DeletedNotesKeep).Command);
        Assert.AreSame(account.RemoveDeletedNotesCommand, VisibleButton(panel, AppStrings.DeletedNotesRemove).Command);
        Assert.IsTrue(TextShown(panel, account.DeletedNotesMessage));
        Assert.IsFalse(Visible(panel, AppStrings.AccountSignInWithGoogle));
        Assert.IsFalse(Visible(panel, AppStrings.DeletedNotesRemoveConfirm));

        // After the server delete this device holds the only copy: Remove is asked twice.
        account.IsConfirmingRemoveDeletedNotes = true;
        panel.UpdateLayout();

        Assert.AreSame(account.RemoveDeletedNotesCommand, VisibleButton(panel, AppStrings.DeletedNotesRemoveConfirm).Command);
        Assert.AreSame(account.CancelRemoveDeletedNotesCommand, VisibleButton(panel, AppStrings.DeletedNotesRemoveCancel).Command);
        Assert.IsTrue(TextShown(panel, account.RemoveDeletedNotesMessage), "The confirmation does not say it cannot be undone.");
        Assert.IsFalse(Visible(panel, AppStrings.DeletedNotesRemove), "The first Remove is still armed beside the confirmation.");
    });

    [TestMethod]
    public void A_database_owned_by_another_account_offers_the_way_back_to_local_notes() => WithAccountPanel((panel, account) =>
    {
        Assert.IsFalse(Visible(panel, AppStrings.ProfileLeaveMismatched));

        account.IsOwnerMismatch = true;
        panel.UpdateLayout();

        Assert.AreSame(account.LeaveMismatchedProfileCommand, VisibleButton(panel, AppStrings.ProfileLeaveMismatched).Command);
    });

    [TestMethod]
    public void A_stalled_switch_offers_to_try_again() => WithAccountPanel((panel, account) =>
    {
        account.NotifyProfileSwitchFailed();
        panel.UpdateLayout();

        Assert.AreSame(account.RetryProfileSwitchCommand, VisibleButton(panel, AppStrings.ProfileSwitchRetry).Command);
        Assert.IsFalse(Visible(panel, AppStrings.AccountSignInWithGoogle), "Sign-in was offered over a half-finished switch.");
    });

    private static bool Visible(AccountPanel panel, string text) =>
        panel.GetLogicalDescendants().OfType<Button>().Any(button => Equals(button.Content, text) && button.IsEffectivelyVisible);

    private static Button VisibleButton(AccountPanel panel, string text) =>
        panel.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, text) && button.IsEffectivelyVisible);

    private static bool TextShown(AccountPanel panel, string text) =>
        panel.GetLogicalDescendants().OfType<TextBlock>().Any(block => block.Text == text && block.IsEffectivelyVisible);

    /// <summary>The real shell with a sync endpoint, its account card open, and binding errors collected.</summary>
    private static void WithAccountPanel(Action<AccountPanel, AccountViewModel> body)
    {
        string? previousEndpoint = Environment.GetEnvironmentVariable("DAYNOTE_SYNC_ENDPOINT");
        string dataRoot = Path.Combine(Path.GetTempPath(), "daynote-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);
        List<string> errors = [];

        HeadlessAppFixture.OnUiThread(() =>
        {
            Environment.SetEnvironmentVariable("DAYNOTE_SYNC_ENDPOINT", "https://example.invalid/");
            Environment.SetEnvironmentVariable("DAYNOTE_DATA_ROOT", dataRoot);

            var services = new ServiceCollection();
            services.AddDaynoteDesktop(DaynoteAppOptions.ForCurrentUser(), Application.Current!, () => null, () => { });
            ServiceProvider provider = services.BuildServiceProvider();
            var shell = provider.GetRequiredService<DesktopShellViewModel>();
            var window = new MainWindow { DataContext = shell };
            ILogSink? previousSink = Logger.Sink;
            try
            {
                Logger.Sink = new BindingErrors(errors);
                shell.OpenAccountCommand.Execute(null);
                window.Width = 1240;
                window.Height = 780;
                window.Show();
                window.UpdateLayout();

                AccountPanel panel = window.GetLogicalDescendants().OfType<AccountPanel>().Single();
                body(panel, shell.Account!);
            }
            finally
            {
                window.Close();
                Logger.Sink = previousSink;
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
                Environment.SetEnvironmentVariable("DAYNOTE_SYNC_ENDPOINT", previousEndpoint);
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

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            errors.Distinct().ToArray(),
            $"Binding errors in the account card:{Environment.NewLine}{string.Join(Environment.NewLine, errors.Distinct())}");
    }

    private sealed class BindingErrors(List<string> errors) : ILogSink
    {
        public bool IsEnabled(LogEventLevel level, string area) =>
            area == LogArea.Binding && level >= LogEventLevel.Warning;

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
            errors.Add(messageTemplate);

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues) =>
            errors.Add($"{messageTemplate} [{string.Join(", ", propertyValues.Select(v => v?.ToString() ?? "null"))}]");
    }
}
