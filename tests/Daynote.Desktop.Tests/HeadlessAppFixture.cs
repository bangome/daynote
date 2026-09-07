using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Daynote.Desktop;

namespace Daynote.Desktop.Tests;

/// <summary>
/// One headless Avalonia application for the whole test run.
/// </summary>
/// <remarks>
/// Avalonia can only be initialised once per process, and every control it builds has to be touched
/// from the thread that owns the dispatcher — so the session is a static, started on first use and
/// shared. <see cref="OnUiThread"/> is how a test gets onto that thread.
/// <para>
/// Headless means Avalonia composes and lays out with no window server at all, which is what lets
/// these run on Windows CI and macOS CI alike. It is the same reason they can cover the shell that
/// is meant to replace the WPF app on Windows while staying the macOS app: one suite, both futures.
/// </para>
/// </remarks>
internal static class HeadlessAppFixture
{
    private static readonly Lock Gate = new();
    private static bool _started;

    /// <summary>Runs <paramref name="work"/> on Avalonia's UI thread and rethrows anything it raised.</summary>
    internal static void OnUiThread(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        EnsureStarted();
        Dispatcher.UIThread.Invoke(work);
    }

    private static void EnsureStarted()
    {
        lock (Gate)
        {
            if (_started)
            {
                return;
            }

            AppBuilder
                .Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
                .SetupWithoutStarting();

            _started = true;
        }
    }
}
