using Avalonia;
using Avalonia.Controls;
using Avalonia.Logging;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Daynote.Mobile.ViewModels;
using Daynote.Mobile.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// The phone shell measures and arranges at handset size with no data-binding errors, in either
/// theme.
/// </summary>
/// <remarks>
/// The same guard the desktop shell has, for the same reason: a failed binding is silent by design —
/// the control keeps its default and the screen looks plausible — so renaming a view-model property
/// breaks the UI without breaking the build. That risk is higher here than anywhere else in the
/// repo, because these views bind to view models that live in another project and are shared with
/// two other apps.
/// </remarks>
[TestClass]
public sealed class MobileShellCompositionTests
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

            var shell = TestServices.Build(data.Path, application).GetRequiredService<MobileShellViewModel>();

            // Built before the sink is attached, as the desktop test does: a control created during
            // InitializeComponent evaluates its $parent bindings while the view still has no
            // DataContext, which every real run also produces and then resolves.
            var view = new MainView { DataContext = shell };

            // A window, because Avalonia only applies styles and templates inside a tree rooted at a
            // TopLevel. The size is an iPhone 14/15/16 in logical points, the narrowest mainstream
            // handset: anything that composes here composes on the Android field too.
            var host = new Window { Content = view, Width = 390, Height = 844 };

            ILogSink? previous = Logger.Sink;
            Logger.Sink = new BindingErrorSink(errors);
            try
            {
                host.Show();
                host.UpdateLayout();

                Assert.IsGreaterThan(0, view.Bounds.Height);
            }
            finally
            {
                Logger.Sink = previous;
                host.Close();
            }
        });

        Assert.IsEmpty(errors, $"Binding errors in the {variantName} theme:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
    }

    /// <summary>Every page is reachable, and exactly one of them is on screen at a time.</summary>
    [TestMethod]
    public void Each_tab_shows_exactly_one_page()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            foreach (MobilePage page in Enum.GetValues<MobilePage>())
            {
                shell.GoToPageCommand.Execute(page);
                view.UpdateLayout();

                List<UserControl> shown = view.GetLogicalDescendants()
                    .OfType<UserControl>()
                    .Where(control => control is DayPage or SearchPage or ListsPage or SettingsPage)
                    .Where(control => control.IsVisible)
                    .ToList();

                Assert.HasCount(1, shown, $"{page} showed {shown.Count} pages.");
            }
        });
    }

    /// <summary>
    /// The calendar really loaded, rather than rendering an empty grid under a weekday header.
    /// </summary>
    [TestMethod]
    public void The_day_page_shows_a_loaded_month()
    {
        TestServices.WithInitialisedShell((_, shell) =>
        {
            // Whole weeks, and at least the four a February can be. The exact row count varies
            // with the month, which is why this asserts the shape rather than a number.
            Assert.IsGreaterThanOrEqualTo(28, shell.Calendar.Cells.Count);
            Assert.AreEqual(0, shell.Calendar.Cells.Count % 7, "The month grid is not whole weeks.");
            Assert.IsNotEmpty(shell.DayLabel, "The day heading is empty, so the shell never settled.");
        });
    }

    /// <summary>
    /// The editor opens over the day and the back arrow closes it, which is the whole navigation
    /// model the desktop does not have.
    /// </summary>
    [TestMethod]
    public void A_new_note_opens_the_editor_and_back_closes_it()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            Assert.IsFalse(shell.IsEditorOpen, "The editor was already open before anything was created.");

            Pump(() => shell.NewNoteCommand.ExecuteAsync(null));
            view.UpdateLayout();

            Assert.IsTrue(shell.IsEditorOpen, "Creating a note did not open the editor.");
            Assert.IsTrue(shell.HasOpenNote, "The editor is up but no note is selected.");
            Assert.IsTrue(
                view.GetLogicalDescendants().OfType<EditorPage>().Single().IsVisible,
                "The editor view model says open but the page is not on screen.");

            Pump(() => shell.CloseEditorAsync());
            view.UpdateLayout();

            Assert.IsFalse(shell.IsEditorOpen, "Going back did not close the editor.");
        });
    }

    /// <summary>Runs an async command to completion on the dispatcher the UI is on.</summary>
    private static void Pump(Func<Task> work)
    {
        Task task = work();
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted)
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, "The command did not complete within 20 seconds.");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        task.GetAwaiter().GetResult();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Collects Avalonia's binding failures, which it reports through the logger.</summary>
    private sealed class BindingErrorSink(List<string> errors) : ILogSink
    {
        public bool IsEnabled(LogEventLevel level, string area) =>
            level >= LogEventLevel.Warning && area == LogArea.Binding;

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
        {
            if (IsEnabled(level, area))
            {
                errors.Add(messageTemplate);
            }
        }

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] values)
        {
            if (IsEnabled(level, area))
            {
                errors.Add($"{messageTemplate} [{string.Join(", ", values)}]");
            }
        }
    }
}
