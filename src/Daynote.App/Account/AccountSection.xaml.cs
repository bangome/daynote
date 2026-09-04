using System.Windows;

namespace Daynote.App.Account;

/// <summary>
/// Code-behind for the account row in settings.
/// </summary>
/// <remarks>
/// The single handler opens the account window through <see cref="IAccountHost"/>. The settings
/// panel is hosted inside the shell window, so the host is the same object the titlebar menu asks —
/// which is what keeps one account window rather than one per entry point.
/// </remarks>
public partial class AccountSection : System.Windows.Controls.UserControl
{
    public AccountSection()
    {
        InitializeComponent();
    }

    private void OnManage(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as IAccountHost)?.ShowAccountWindow();
}
