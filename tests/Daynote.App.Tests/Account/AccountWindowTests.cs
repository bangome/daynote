using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Diagnostics;
using Daynote.App.Account;
using Daynote.App.Shell;
using Daynote.App.Shell.Product;
using Daynote.App.Showcase;
using Daynote.Core.Sync;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Account;

/// <summary>
/// The account window composes in both states, both themes, with zero binding errors.
/// </summary>
/// <remarks>
/// Worth its own harness because this window carries the billing surface: a binding that silently
/// fails here does not draw a blank label, it draws a price or a renewal date that is not there. The
/// listener treats any data-binding warning as a failure for exactly that reason.
/// </remarks>
[TestClass]
public sealed class AccountWindowTests
{
    [STATestMethod]
    [DataRow(false, false, DisplayName = "signed out, light")]
    [DataRow(true, false, DisplayName = "signed in, light")]
    [DataRow(true, true, DisplayName = "signed in, dark")]
    public void The_window_composes_without_binding_errors(bool signedIn, bool dark)
    {
        Application application = EnsureResources(dark);
        var accounts = new FakeAccounts();
        AccountViewModel account = Build(accounts);

        if (signedIn)
        {
            account.SignInCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        }

        var listener = new BindingErrorListener();
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning | SourceLevels.Error;
        try
        {
            var window = new AccountWindow(account);
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(520, 900));
            content.Arrange(new Rect(0, 0, 520, 900));
            ApplyTemplates(content);
            content.UpdateLayout();

            Assert.IsGreaterThan(0, content.DesiredSize.Height);
            CollectionAssert.AreEqual(
                Array.Empty<string>(),
                listener.Errors.ToArray(),
                $"Binding errors:{Environment.NewLine}{string.Join(Environment.NewLine, listener.Errors)}");
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
            application.Resources.MergedDictionaries.Clear();
        }
    }

    /// <summary>
    /// The design mock draws a card-number form inside the app. It must never be built: the payment
    /// provider is the merchant of record precisely so Daynote never handles card data, and Store
    /// policy 10.8.2 wants the purchase completed in the browser. So no surface asks for a card —
    /// not the markup, and not the string catalogs a future screen would pull its labels from.
    /// </summary>
    [TestMethod]
    public void Nothing_in_the_app_asks_for_a_card()
    {
        string[] forbidden = ["CVC", "cvc", "Card number", "카드 번호", "cardNumber"];

        string[] offenders = [.. Directory
            .EnumerateFiles(TestPaths.AppRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => forbidden.Any(term => File.ReadAllText(path).Contains(term, StringComparison.Ordinal)))
            .Select(path => Path.GetRelativePath(TestPaths.RepositoryRoot, path))];

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            offenders,
            "Checkout opens in the browser at the payment provider. Nothing in the app may collect "
                + $"card details: {string.Join(", ", offenders)}");
    }

    private static AccountViewModel Build(FakeAccounts accounts) => new(
        accounts.Service,
        accounts.Store,
        () => ValueTask.FromResult(SyncReport.For(SyncOutcome.Completed)),
        new AccountViewModelTests.FakeExporter(),
        _ => { },
        @"C:\conflicts");

    private static Application EnsureResources(bool dark)
    {
        Application application = Application.Current ?? new Application();
        application.Resources.MergedDictionaries.Clear();
        ShowcaseResources.Load(application, highContrast: false);
        application.Resources["Daynote.Convert.BoolToVisibility"] = new BooleanToVisibilityConverter();
        application.Resources["Daynote.Convert.InverseBool"] = new InverseBooleanConverter();
        application.Resources["Daynote.Convert.NullToVisibility"] = new NullToVisibilityConverter();
        application.Resources["Daynote.Convert.NullToCollapsed"] = new NullToVisibilityConverter { Invert = true };
        application.Resources["Daynote.Convert.InverseBoolToVisibility"] = new InverseBoolToVisibilityConverter();
        new WpfProductThemeApplier(application).Apply(dark);
        return application;
    }

    private static void ApplyTemplates(DependencyObject root)
    {
        if (root is Control control)
        {
            control.ApplyTemplate();
        }

        for (int index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            ApplyTemplates(System.Windows.Media.VisualTreeHelper.GetChild(root, index));
        }
    }

    private sealed class BindingErrorListener : TraceListener
    {
        public List<string> Errors { get; } = [];

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                Errors.Add(message);
            }
        }
    }
}
