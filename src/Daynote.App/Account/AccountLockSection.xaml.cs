using System.Windows;

namespace Daynote.App.Account;

/// <summary>
/// Code-behind for the note-lock section.
/// </summary>
/// <remarks>
/// The two click handlers exist because the lock passphrase comes from a <see cref="System.Windows.Controls.PasswordBox"/>,
/// which deliberately has no bindable value: routing it through a view-model property would put the
/// passphrase in a field that could be serialised, logged, or captured in a crash dump. It is read at
/// the moment of the command, used to derive a key, and cleared. Everything else here is an ordinary
/// command binding — there is no password in Daynote's sign-in at all.
/// </remarks>
public partial class AccountLockSection : System.Windows.Controls.UserControl
{
    public AccountLockSection()
    {
        InitializeComponent();
    }

    private void OnEnableLock(object sender, RoutedEventArgs e)
    {
        if (DataContext is AccountViewModel viewModel)
        {
            viewModel.EnableLockCommand.Execute(NewPassphraseBox.Password);
            NewPassphraseBox.Clear();
        }
    }

    private void OnUnlock(object sender, RoutedEventArgs e)
    {
        if (DataContext is AccountViewModel viewModel)
        {
            viewModel.UnlockCommand.Execute(UnlockPassphraseBox.Password);
            UnlockPassphraseBox.Clear();
        }
    }
}
