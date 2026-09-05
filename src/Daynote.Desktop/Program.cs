using Avalonia;
using Daynote.Infrastructure.Instance;

namespace Daynote.Desktop;

internal static class Program
{
    private const string InstanceBaseName = "Daynote";

    /// <summary>The primary claim, handed to <see cref="App"/> so Quit can release it before shutdown.</summary>
    internal static SingleInstanceCoordinator? SingleInstance { get; private set; }

    // Nothing Avalonia-dependent may run before the AppBuilder starts; the single-instance handshake is
    // plain sockets and a lock file, so it is safe here and keeps a second launch from ever painting.
    [STAThread]
    public static int Main(string[] args)
    {
        SingleInstance = SingleInstanceCoordinator.ForCurrentUserPortable(InstanceBaseName);
        if (SingleInstance.Start() == SingleInstanceRole.Secondary)
        {
            SingleInstance.ActivatePrimaryAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            SingleInstance.DisposeAsync().AsTask().GetAwaiter().GetResult();
            SingleInstance = null;
            return 0;
        }

        int exitCode;
        try
        {
            exitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            SingleInstance?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            SingleInstance = null;
        }

        // A staged restore applies on the next start; relaunch now that the primary claim is released.
        if (App.RelaunchAfterExit && Environment.ProcessPath is { Length: > 0 } executable)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = false })?.Dispose();
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // The restore still applies when the user next opens Daynote.
            }
        }

        return exitCode;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
