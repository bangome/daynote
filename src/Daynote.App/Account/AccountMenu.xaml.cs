using System.Windows;

namespace Daynote.App.Account;

/// <summary>The window that can put the account window on screen. Implemented by the shell window.</summary>
public interface IAccountHost
{
    /// <summary>Shows the account window, or brings the open one forward.</summary>
    void ShowAccountWindow();

    /// <summary>Opens the settings panel.</summary>
    void ShowSettingsPanel();
}

/// <summary>
/// Code-behind for the titlebar account button.
/// </summary>
/// <remarks>
/// Both handlers close the menu first: leaving a popup open behind a window that has just appeared
/// looks like the click did two things. Routing through <see cref="IAccountHost"/> rather than
/// constructing the window here keeps one owner for it — a second account window would show the same
/// view model twice and let two copies of the checkout button race.
/// </remarks>
public partial class AccountMenu : System.Windows.Controls.UserControl
{
    public AccountMenu()
    {
        InitializeComponent();
    }

    private void OnOpenAccount(object sender, RoutedEventArgs e)
    {
        Toggle.IsChecked = false;
        Host?.ShowAccountWindow();
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        Toggle.IsChecked = false;
        Host?.ShowSettingsPanel();
    }

    private IAccountHost? Host => Window.GetWindow(this) as IAccountHost;
}
