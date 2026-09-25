using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Daynote.App.Account;
using Daynote.Mobile.ViewModels;

namespace Daynote.Mobile.Views;

/// <summary>
/// The cloud account card. Passphrases are read out of the boxes in code and cleared straight after,
/// never held in a bindable property — the same rule the desktop panel follows.
/// </summary>
public partial class AccountPanel : UserControl
{
    public AccountPanel() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Catalog strings, reached as <c>#Root.Strings</c> from inside the templates.</summary>
    public MobileStrings Strings => MobileStrings.Instance;

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
