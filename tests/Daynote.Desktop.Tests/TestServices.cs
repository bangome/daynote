using Avalonia;
using Avalonia.Threading;
using Daynote.App.Composition;
using Daynote.Desktop.Composition;
using Daynote.Desktop.ViewModels;
using Daynote.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Desktop.Tests;

/// <summary>The app's real service graph, pointed at a throwaway data root.</summary>
internal static class TestServices
{
    internal static ServiceProvider Build(string dataRoot, Application application)
    {
        Environment.SetEnvironmentVariable("DAYNOTE_DATA_ROOT", dataRoot);
        var services = new ServiceCollection();
        services.AddDaynoteDesktop(
            DaynoteAppOptions.ForCurrentUser(),
            application,
            () => null,
            () => { });
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// The shell window, shown and initialised the way App.axaml.cs does it, on the UI thread.
    /// </summary>
    /// <remarks>
    /// Without <c>InitializeAsync</c> the calendar has a weekday header and no days, and a frame
    /// shows an app that has not loaded rather than the app. Its continuations are posted to this
    /// dispatcher, so the loop pumps it until the task completes, then drains what the
    /// initialisation posted for after itself (collection refreshes, summaries) so what the body sees
    /// is the settled UI and not a mid-update one.
    /// </remarks>
    internal static void WithInitialisedShell(Action<MainWindow, DesktopShellViewModel> body) =>
        WithInitialisedShell(1256, 788, body);

    /// <summary>
    /// The same, at a chosen size. The Store wants listing images of at least 1366x768, which is
    /// larger than the window every other test renders at.
    /// </summary>
    internal static void WithInitialisedShell(
        double width,
        double height,
        Action<MainWindow, DesktopShellViewModel> body)
    {
        using var data = new TempDataRoot();

        HeadlessAppFixture.OnUiThread(() =>
        {
            Application application = Application.Current!;
            ServiceProvider provider = TestServices.Build(data.Path, application);
            var shell = provider.GetRequiredService<DesktopShellViewModel>();
            var window = new MainWindow { DataContext = shell, Width = width, Height = height };
            try
            {
                window.Show();

                Task initialising = shell.InitializeAsync();
                DateTime deadline = DateTime.UtcNow.AddSeconds(20);
                while (!initialising.IsCompleted)
                {
                    Assert.IsTrue(DateTime.UtcNow < deadline, "The shell did not finish initialising within 20 seconds.");
                    Dispatcher.UIThread.RunJobs();
                    Thread.Sleep(5);
                }

                initialising.GetAwaiter().GetResult();

                for (int i = 0; i < 20; i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    Thread.Sleep(5);
                }

                window.UpdateLayout();
                body(window, shell);
            }
            finally
            {
                window.Close();
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

}

/// <summary>A throwaway data root, so a test never opens the developer's own database.</summary>
internal sealed class TempDataRoot : IDisposable
{
    internal string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "daynote-tests", Guid.NewGuid().ToString("N"));

    internal TempDataRoot() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A SQLite handle can outlive the test by a moment; a leftover temp folder is not a
            // failure worth turning a green run red.
        }
    }
}
