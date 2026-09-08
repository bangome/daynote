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
/// <para>
/// Drawing is real, not stubbed: <c>UseHeadlessDrawing</c> is off and Skia is on, so a shown window
/// produces actual pixels and <see cref="RenderedFrameTests"/> can capture them. That is what the WPF
/// showcase pipeline was for and what this port needed from it (docs/WINDOWS_ON_AVALONIA.md §5, §7).
/// Skia arrives transitively through the app's Avalonia.Desktop reference; the test project adds no
/// package for it.
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
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

            _started = true;
        }
    }
}
