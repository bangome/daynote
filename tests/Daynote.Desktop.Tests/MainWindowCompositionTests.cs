using Avalonia;
using Avalonia.Controls;
using Avalonia.Logging;
using Avalonia.Styling;
using Daynote.Desktop.ViewModels;
using Daynote.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Desktop.Tests;

/// <summary>
/// The shell window measures and arranges with no data-binding errors, in either theme.
/// </summary>
/// <remarks>
/// The WPF app has had this since early on: build the window, listen for binding failures, fail on
/// any. Avalonia reports them through <see cref="Logger"/> rather than a trace source, so the sink
/// below is the counterpart of the WPF <c>BindingErrorTraceListener</c>.
/// <para>
/// A failed binding is silent by design — the control keeps its default and the app looks fine —
/// which is exactly why it needs a test. Renaming a view-model property, or moving one between the
/// shared presentation layer and the app, breaks bindings without breaking the build.
/// </para>
/// </remarks>
[TestClass]
public sealed class MainWindowCompositionTests
{
    [TestMethod]
    [DataRow("Light")]
    [DataRow("Dark")]
    public void The_shell_composes_without_binding_errors(string variantName)
    {
        using var data = new TempDataRoot();
        List<string> errors = [];

        HeadlessAppFixture.OnUiThread(() =>
        {
            Application application = Application.Current!;
            application.RequestedThemeVariant = variantName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

            ServiceProvider provider = TestServices.Build(data.Path, application);
            var shell = provider.GetRequiredService<DesktopShellViewModel>();

            // Built before the sink is attached, on purpose. A control created during
            // InitializeComponent evaluates its $parent[Window].DataContext bindings while the window
            // still has none — noise every real run also produces and then resolves. Bindings
            // re-evaluate when the DataContext arrives, so a genuine failure still shows up below.
            var window = new MainWindow { DataContext = shell };

            ILogSink? previous = Logger.Sink;
            Logger.Sink = new BindingErrorSink(errors);
            try
            {
                window.Measure(new Size(1240, 780));
                window.Arrange(new Rect(0, 0, 1240, 780));
                window.UpdateLayout();

                Assert.IsGreaterThan(0, window.DesiredSize.Height);
            }
            finally
            {
                Logger.Sink = previous;

                // The shell is IAsyncDisposable only, which the container refuses to dispose
                // synchronously. Blocking here is fine: this is the UI thread of a headless app
                // with no pending work of its own.
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            errors.Distinct().ToArray(),
            $"Binding errors in {variantName}:{Environment.NewLine}{string.Join(Environment.NewLine, errors.Distinct())}");
    }

    [TestMethod]
    public void The_window_carries_the_multi_resolution_icon()
    {
        // Windows asks for the icon at 16 and 32 for the taskbar and Alt-Tab, so the asset is an .ico
        // with authored frames at those sizes rather than the 926px PNG beside it. Asserted because
        // "it decoded" is the only thing that tells us Avalonia read the .ico at all — a resource
        // that fails to load would leave the window iconless rather than throw.
        using var data = new TempDataRoot();

        HeadlessAppFixture.OnUiThread(() =>
        {
            ServiceProvider provider = TestServices.Build(data.Path, Application.Current!);
            var shell = provider.GetRequiredService<DesktopShellViewModel>();
            var window = new MainWindow { DataContext = shell };
            try
            {
                Assert.IsNotNull(window.Icon, "The window has no icon; the .ico did not load.");
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    /// <summary>Collects everything Avalonia logs about bindings; anything at all is a failure.</summary>
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
