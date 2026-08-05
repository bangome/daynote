using System.Drawing;
using System.Runtime.Versioning;
using Daynote.App.Localization;
using WinFormsApp = System.Windows.Forms;

namespace Daynote.App.Lifecycle;

/// <summary>
/// The Windows notification-area presence built on a WinForms <c>NotifyIcon</c>. Its menu mirrors the
/// DESIGN TrayMenu order — Show Daynote, Settings, separator, Quit — and reflects the window-shown
/// state. Menu clicks raise the coordinator's commands.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TrayIconService : ITrayPresenter, ILanguageAware
{
    private readonly WinFormsApp.NotifyIcon _icon;
    private readonly WinFormsApp.ToolStripMenuItem _showItem;
    private readonly WinFormsApp.ToolStripMenuItem _settingsItem;
    private readonly WinFormsApp.ToolStripMenuItem _quitItem;
    private readonly Icon _brandIcon;
    private bool _disposed;

    /// <summary>Loads the Daynote brand tray icon from app resources, falling back to the system icon.</summary>
    private static Icon LoadBrandIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Daynote.App;component/Assets/Brand/daynote-favicon-v1.ico");
            System.Windows.Resources.StreamResourceInfo? info = System.Windows.Application.GetResourceStream(uri);
            if (info?.Stream is { } stream)
            {
                using (stream)
                {
                    return new Icon(stream);
                }
            }
        }
        catch (Exception)
        {
            // Fall through to the system icon below; the tray must never fail to appear over branding.
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    public TrayIconService()
    {
        _showItem = new WinFormsApp.ToolStripMenuItem(Localization.AppStrings.TrayShow);
        _showItem.Click += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);

        _settingsItem = new WinFormsApp.ToolStripMenuItem(Localization.AppStrings.TraySettings);
        _settingsItem.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        _quitItem = new WinFormsApp.ToolStripMenuItem(Localization.AppStrings.TrayQuit);
        _quitItem.Click += (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty);

        var menu = new WinFormsApp.ContextMenuStrip();
        menu.Items.Add(_showItem);
        menu.Items.Add(_settingsItem);
        menu.Items.Add(new WinFormsApp.ToolStripSeparator());
        menu.Items.Add(_quitItem);

        _brandIcon = LoadBrandIcon();
        _icon = new WinFormsApp.NotifyIcon
        {
            Text = Localization.AppStrings.TrayAppName,
            Icon = _brandIcon,
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);

        LocalizationService.Instance.Observe(this);
    }

    public event EventHandler? ShowRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? QuitRequested;

    /// <summary>Re-renders the tray captions; a disposed icon is left alone.</summary>
    void ILanguageAware.OnLanguageChanged()
    {
        if (_disposed)
        {
            return;
        }

        _showItem.Text = Localization.AppStrings.TrayShow;
        _settingsItem.Text = Localization.AppStrings.TraySettings;
        _quitItem.Text = Localization.AppStrings.TrayQuit;
        _icon.Text = Localization.AppStrings.TrayAppName;
    }

    public void UpdateWindowShown(bool shown) => _showItem.Enabled = !shown;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
        _brandIcon.Dispose();
    }
}
