namespace Daynote.App.Lifecycle;

/// <summary>The window surface the coordinator drives: hide-to-tray, show/activate, open settings.</summary>
public interface IWindowHost
{
    /// <summary>Hides the window to the tray. The process and clipboard listener remain alive.</summary>
    void HideToTray();

    /// <summary>Restores and activates the window (tray Show, or a secondary launch's activation).</summary>
    void ShowAndActivate();

    /// <summary>Shows the window and opens the settings surface.</summary>
    void ShowSettings();
}

/// <summary>
/// The notification-area presence: reflects window state and raises the menu commands. The real
/// implementation is a WinForms <c>NotifyIcon</c>; tests use a recording double.
/// </summary>
public interface ITrayPresenter : IDisposable
{
    void UpdateWindowShown(bool shown);

    event EventHandler? ShowRequested;

    event EventHandler? SettingsRequested;

    event EventHandler? QuitRequested;
}

/// <summary>Requests explicit application shutdown (real = WPF <c>Application.Shutdown</c>).</summary>
public interface IApplicationExit
{
    void Shutdown();
}
