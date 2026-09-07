using Avalonia;
using Daynote.App.Composition;
using Daynote.App.Shell.Product;
using Daynote.Desktop.Composition;
using Daynote.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Desktop.Tests;

/// <summary>
/// Navigating out of the timeline, on the Avalonia shell.
/// </summary>
/// <remarks>
/// The pair of the WPF <c>TimelineNavigationTests</c>. The two shells carry their own copy of this
/// navigation code — <c>DesktopShellViewModel</c> and <c>ProductShellViewModel</c> are separate
/// classes — so a behaviour that has to hold in both needs asserting in both, or one of them drifts.
/// <para>
/// This runs on the headless fixture only because composing the shell wants an
/// <see cref="Application"/>; nothing here draws.
/// </para>
/// </remarks>
[TestClass]
public sealed class TimelineNavigationTests
{
    [TestMethod]
    public void A_calendar_day_leaves_the_timeline()
    {
        WithShell(shell =>
        {
            shell.ToggleTimelineCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Assert.IsTrue(shell.IsTimelineMode, "The timeline did not open.");

            CalendarDayCellViewModel cell = shell.Calendar.Cells.First(c => c.IsInMonth && !c.IsSelected);
            cell.SelectCommand.ExecuteAsync(null).GetAwaiter().GetResult();

            Assert.IsFalse(shell.IsTimelineMode, "Picking a day left the timeline open.");
            Assert.AreEqual(cell.Date, shell.SelectedDate);
        });
    }

    [TestMethod]
    public void The_day_already_selected_still_leaves_the_timeline()
    {
        WithShell(shell =>
        {
            shell.ToggleTimelineCommand.ExecuteAsync(null).GetAwaiter().GetResult();

            CalendarDayCellViewModel cell = shell.Calendar.Cells.Single(c => c.IsInMonth && c.IsSelected);
            cell.SelectCommand.ExecuteAsync(null).GetAwaiter().GetResult();

            Assert.IsFalse(shell.IsTimelineMode);
        });
    }

    [TestMethod]
    public void The_timeline_can_be_reopened_after_leaving_it()
    {
        WithShell(shell =>
        {
            shell.ToggleTimelineCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            shell.Calendar.Cells.First(c => c.IsInMonth && !c.IsSelected)
                .SelectCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            shell.ToggleTimelineCommand.ExecuteAsync(null).GetAwaiter().GetResult();

            Assert.IsTrue(shell.IsTimelineMode, "The timeline could not be reopened.");
        });
    }

    /// <summary>Composes the shell over a throwaway database and initialises it.</summary>
    private static void WithShell(Action<DesktopShellViewModel> body)
    {
        string dataRoot = Path.Combine(Path.GetTempPath(), "daynote-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);

        HeadlessAppFixture.OnUiThread(() =>
        {
            Environment.SetEnvironmentVariable("DAYNOTE_DATA_ROOT", dataRoot);
            Application application = Application.Current!;
            var services = new ServiceCollection();
            services.AddDaynoteDesktop(DaynoteAppOptions.ForCurrentUser(), application, () => null, () => { });
            ServiceProvider provider = services.BuildServiceProvider();

            var shell = provider.GetRequiredService<DesktopShellViewModel>();
            try
            {
                shell.InitializeAsync().GetAwaiter().GetResult();
                body(shell);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
}
