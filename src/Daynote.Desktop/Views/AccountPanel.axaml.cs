using Avalonia.Controls;
using Avalonia.Interactivity;
using Daynote.App.Account;
using Daynote.Desktop.ViewModels;

namespace Daynote.Desktop.Views;

/// <summary>
/// Hosts the account view model. The two click handlers exist because the passphrase comes from a
/// masked box and is handed straight to the command, then cleared, instead of living in a bound property.
/// </summary>
public partial class AccountPanel : UserControl
{
    public AccountPanel()
    {
        InitializeComponent();
    }

    public AppStringsProxy Strings => AppStringsProxy.Instance;

    private void OnUnlock(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AccountViewModel account)
        {
            string? passphrase = account.IsUsingRecoveryKey ? null : UnlockPassphraseBox.Text;
            UnlockPassphraseBox.Text = string.Empty;
            account.UnlockCommand.Execute(passphrase);
        }
    }

    private void OnEnableLock(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AccountViewModel account)
        {
            string? passphrase = NewPassphraseBox.Text;
            NewPassphraseBox.Text = string.Empty;
            account.EnableLockCommand.Execute(passphrase);
        }
    }
}
