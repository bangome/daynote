using System.Windows;

namespace Daynote.App.Account;

/// <summary>
/// The account window. Owns no state: it hosts the shared <see cref="AccountViewModel"/>, the same
/// instance the titlebar avatar and the settings row read, so there is one answer to "am I signed in"
/// wherever it is asked.
/// </summary>
public partial class AccountWindow : Window
{
    public AccountWindow(AccountViewModel account)
    {
        ArgumentNullException.ThrowIfNull(account);
        InitializeComponent();
        DataContext = account;
    }

    /// <summary>
    /// Re-reads the subscription each time the window opens. The entitlement can change without the
    /// app doing anything — a renewal, a failed charge, a trial running out — so the state on screen
    /// has to be fetched rather than remembered from the last visit.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (DataContext is AccountViewModel account)
        {
            account.RefreshBillingCommand.Execute(null);
        }
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
