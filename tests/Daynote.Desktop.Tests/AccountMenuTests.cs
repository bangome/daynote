using Avalonia;
using Avalonia.Controls;
using Avalonia.Logging;
using Avalonia.LogicalTree;
using Daynote.App.Composition;
using Daynote.Desktop.Composition;
using Daynote.Desktop.ViewModels;
using Daynote.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Desktop.Tests;

/// <summary>
/// The titlebar account button and the menu under it.
/// </summary>
/// <remarks>
/// <see cref="MainWindowCompositionTests"/> cannot reach this control. It composes the shell with no
/// sync endpoint, so <c>Account</c> is null, the menu is never created and the gear stands in its
/// place — which is a state worth having, and is the reason the menu needs its own test with an
/// endpoint configured.
/// <para>
/// The menu's contents are a flyout, so they are not in the tree until it is shown. Everything
/// inside — the identity row, the sync row, the two actions — binds through the account or through
/// the window, and none of it is evaluated until then. Showing it here is the only way those
/// bindings get exercised at all.
/// </para>
/// </remarks>
[TestClass]
public sealed class AccountMenuTests
{
    [TestMethod]
    public void The_titlebar_shows_the_account_menu_when_the_build_has_an_endpoint()
    {
        WithShell((window, _) =>
        {
            Assert.IsNotNull(FindMenu(window), "No AccountMenu in the shell.");
            Assert.IsTrue(FindMenu(window)!.IsVisible, "The account menu is present but hidden.");
        });
    }

    [TestMethod]
    public void The_menu_opens_and_binds_without_errors()
    {
        List<string> errors = [];

        WithShell((window, sink) =>
        {
            AccountMenu menu = FindMenu(window)!;
            var toggle = (Button)menu.GetLogicalChildren().Single();

            sink(errors);
            toggle.Flyout!.ShowAt(toggle);
            window.UpdateLayout();

            // The flyout's popup root is separate from the window, so its contents have to be laid
            // out on their own before anything inside has been measured.
            var content = (Control)((Flyout)toggle.Flyout!).Content!;
            content.Measure(new Size(320, 400));
            content.Arrange(new Rect(0, 0, 320, 400));

            Assert.IsGreaterThan(0, content.Bounds.Height, "The menu measured to nothing.");
        });

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            errors.Distinct().ToArray(),
            $"Binding errors in the account menu:{Environment.NewLine}{string.Join(Environment.NewLine, errors.Distinct())}");
    }

    private static AccountMenu? FindMenu(Window window) =>
        window.GetLogicalDescendants().OfType<AccountMenu>().FirstOrDefault();

    /// <summary>
    /// Composes the shell with a sync endpoint set, so the account view model exists. The endpoint is
    /// never called: nothing in these tests signs in.
    /// </summary>
    private static void WithShell(Action<Window, Action<List<string>>> body)
    {
        string? previousEndpoint = Environment.GetEnvironmentVariable("DAYNOTE_SYNC_ENDPOINT");
        string dataRoot = Path.Combine(Path.GetTempPath(), "daynote-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);

        HeadlessAppFixture.OnUiThread(() =>
        {
            Environment.SetEnvironmentVariable("DAYNOTE_SYNC_ENDPOINT", "https://example.invalid/");
            Environment.SetEnvironmentVariable("DAYNOTE_DATA_ROOT", dataRoot);

            Application application = Application.Current!;
            var services = new ServiceCollection();
            services.AddDaynoteDesktop(DaynoteAppOptions.ForCurrentUser(), application, () => null, () => { });
            ServiceProvider provider = services.BuildServiceProvider();

            var shell = provider.GetRequiredService<DesktopShellViewModel>();
            Assert.IsTrue(shell.HasAccount, "The endpoint is set but the shell has no account.");

            var window = new MainWindow { DataContext = shell };
            ILogSink? previousSink = Logger.Sink;
            try
            {
                window.Measure(new Size(1240, 780));
                window.Arrange(new Rect(0, 0, 1240, 780));
                window.UpdateLayout();

                body(window, errors => Logger.Sink = new BindingErrorSink(errors));
            }
            finally
            {
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
    }

    private sealed class BindingErrorSink(List<string> errors) : ILogSink
    {
        public bool IsEnabled(LogEventLevel level, string area) =>
            area == LogArea.Binding && level >= LogEventLevel.Warning;

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
            Capture(area, messageTemplate, []);

        public void Log(
            LogEventLevel level,
            string area,
            object? source,
            string messageTemplate,
            params object?[] propertyValues) =>
            Capture(area, messageTemplate, propertyValues);

        private void Capture(string area, string template, object?[] values)
        {
            if (area != LogArea.Binding)
            {
                return;
            }

            errors.Add(values.Length == 0
                ? template
                : $"{template} [{string.Join(", ", values.Select(v => v?.ToString() ?? "null"))}]");
        }
    }
}
