using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Daynote.Mobile;

namespace Daynote.Mobile.Tests;

/// <summary>
/// One headless Avalonia application for the whole test run, the phone counterpart of the desktop
/// fixture.
/// </summary>
/// <remarks>
/// Avalonia can only be initialised once per process and every control has to be touched from the
/// thread that owns the dispatcher, so the session is static and shared.
/// <para>
/// <c>SetupWithoutStarting</c> means <c>OnFrameworkInitializationCompleted</c> never runs, so
/// <c>App.Platform</c> is not needed: these tests compose the service graph themselves and set a
/// view's DataContext directly, which is what isolates the UI from the two heads.
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
