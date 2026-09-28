using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Daynote.App.Account;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.Core.Sync;
using Daynote.Infrastructure.Sync;
using Daynote.Mobile.Composition;
using Daynote.Mobile.ViewModels;
using Daynote.Mobile.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// The two things on the account card that store review checks for: Sign in with Apple beside
/// Google on iOS (App Store 4.8), and a way to delete the account from inside the app (App Store
/// 5.1.1(v), Google Play's account deletion policy).
/// </summary>
[TestClass]
public sealed class AccountPanelTests
{
    [TestMethod]
    public void Sign_in_with_Apple_is_offered_where_the_platform_has_it() => WithAccountPanel(new NoApple(), (panel, _) =>
    {
        Button apple = AppleButton(panel);
        Button google = ButtonReading(panel, AppStrings.AccountSignInWithGoogle);

        Assert.IsTrue(apple.IsEffectivelyVisible, "The Apple button is hidden on a platform that has it.");
        Assert.IsTrue(google.IsEffectivelyVisible);
        Assert.IsGreaterThanOrEqualTo(google.Bounds.Height, apple.Bounds.Height, "Apple's button is smaller than Google's.");
    });

    [TestMethod]
    public void Sign_in_with_Apple_is_absent_where_the_platform_has_not_got_it() => WithAccountPanel(null, (panel, account) =>
    {
        Assert.IsTrue(account.IsPhone, "The phone's account card would offer a subscription it cannot sell.");
        Assert.IsFalse(account.OffersSubscription);
        Assert.IsFalse(AppleButton(panel).IsEffectivelyVisible);
        Assert.IsTrue(ButtonReading(panel, AppStrings.AccountSignInWithGoogle).IsEffectivelyVisible);
    });

    [TestMethod]
    public void Deleting_asks_first() => WithAccountPanel(null, (panel, account) =>
    {
        // Signed in, as far as the card is concerned.
        account.SignedInEmail = "someone@example.com";
        Dispatcher.UIThread.RunJobs();

        Button delete = ButtonReading(panel, AppStrings.AccountDelete);
        Assert.IsTrue(delete.IsEffectivelyVisible, "A signed-in account has no way to delete itself.");
        Assert.IsFalse(ButtonReading(panel, AppStrings.AccountDeleteConfirm).IsEffectivelyVisible, "The irreversible button shows before anyone asked.");

        delete.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.IsTrue(ButtonReading(panel, AppStrings.AccountDeleteConfirm).IsEffectivelyVisible);
        Assert.IsTrue(ButtonReading(panel, AppStrings.AccountDeleteCancel).IsEffectivelyVisible);

        ButtonReading(panel, AppStrings.AccountDeleteCancel).Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.IsFalse(ButtonReading(panel, AppStrings.AccountDeleteConfirm).IsEffectivelyVisible, "Cancel left the delete button armed.");
    });

    /// <summary>The Apple button carries a logo beside its title, so it is found by its class.</summary>
    private static Button AppleButton(AccountPanel panel) =>
        panel.GetLogicalDescendants().OfType<Button>().Single(button => button.Classes.Contains("apple"));

    private static Button ButtonReading(AccountPanel panel, string text) =>
        panel.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, text));

    /// <summary>The settings page of the real phone graph, with sync on and nobody signed in.</summary>
    private static void WithAccountPanel(IAppleIdentityProvider? apple, Action<AccountPanel, AccountViewModel> body)
    {
        string root = Path.Combine(Path.GetTempPath(), "daynote-mobile-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            HeadlessAppFixture.OnUiThread(() =>
            {
                var services = new ServiceCollection();
                services.AddDaynoteMobile(
                    new DaynoteAppOptions(root) { SyncEndpoint = new Uri("https://sync.invalid") },
                    Application.Current!,
                    () => null,
                    TestServices.PlatformFor(root) with
                    {
                        SecretProtector = new XorProtector(),
                        Identity = new NoGoogle(),
                        AppleIdentity = apple,
                    });
                ServiceProvider provider = services.BuildServiceProvider();
                try
                {
                    var shell = provider.GetRequiredService<MobileShellViewModel>();
                    var view = new MainView { DataContext = shell };
                    var host = new Window { Content = view, Width = 390, Height = 844 };
                    host.Show();
                    Task initialising = shell.InitializeAsync();
                    DateTime deadline = DateTime.UtcNow.AddSeconds(20);
                    while (!initialising.IsCompleted)
                    {
                        Assert.IsTrue(DateTime.UtcNow < deadline, "The shell did not initialise.");
                        Dispatcher.UIThread.RunJobs();
                        Thread.Sleep(5);
                    }

                    shell.GoToPageCommand.Execute(MobilePage.Settings);
                    Dispatcher.UIThread.RunJobs();
                    view.UpdateLayout();

                    AccountPanel panel = view.GetLogicalDescendants().OfType<AccountPanel>().Single();
                    body(panel, shell.Account!);
                    host.Close();
                }
                finally
                {
                    provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            });
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class NoApple : IAppleIdentityProvider
    {
        public ValueTask<AppleIdentityGrant> AuthorizeAsync(string nonceSha256Hex, CancellationToken cancellationToken = default) =>
            throw new AccountException(AccountFailure.SignInCancelled, "Not used by these tests.");
    }

    private sealed class NoGoogle : IIdentityProvider
    {
        public ValueTask<IdentityGrant> AuthorizeAsync(CancellationToken cancellationToken = default) =>
            throw new AccountException(AccountFailure.SignInCancelled, "Not used by these tests.");
    }

    private sealed class XorProtector : ISecretProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> entropy) => Xor(plaintext);

        public byte[] Unprotect(ReadOnlySpan<byte> sealedBytes, ReadOnlySpan<byte> entropy) => Xor(sealedBytes);

        private static byte[] Xor(ReadOnlySpan<byte> input)
        {
            byte[] output = input.ToArray();
            for (int i = 0; i < output.Length; i++)
            {
                output[i] ^= 0x5A;
            }

            return output;
        }
    }
}
