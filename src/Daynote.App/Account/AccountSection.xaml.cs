using System.Windows;

namespace Daynote.App.Account;

/// <summary>
/// Code-behind for the cloud-sync settings section.
/// </summary>
/// <remarks>
/// The two click handlers exist because the password comes from a <see cref="PasswordBox"/>, which
/// deliberately has no bindable value: routing it through a view-model property would put the
/// password in a field that could be serialised, logged, or captured in a crash dump. It is read at
/// the moment of the command and not retained.
/// </remarks>
public partial class AccountSection : System.Windows.Controls.UserControl
{
    public AccountSection()
    {
        InitializeComponent();
    }

    private void OnSignIn(object sender, RoutedEventArgs e) => Invoke(static (vm, password) =>
        vm.SignInCommand.Execute(password));

    private void OnRegister(object sender, RoutedEventArgs e) => Invoke(static (vm, password) =>
        vm.RegisterCommand.Execute(password));

    private void OnConfirmReset(object sender, RoutedEventArgs e)
    {
        if (DataContext is AccountViewModel viewModel)
        {
            viewModel.ConfirmResetCommand.Execute(ResetPasswordBox.Password);
            ResetPasswordBox.Clear();
        }
    }

    private void OnUnlock(object sender, RoutedEventArgs e)
    {
        if (DataContext is AccountViewModel viewModel)
        {
            viewModel.UnlockCommand.Execute(UnlockPasswordBox.Password);
            UnlockPasswordBox.Clear();
        }
    }

    private void Invoke(Action<AccountViewModel, string> action)
    {
        if (DataContext is not AccountViewModel viewModel)
        {
            return;
        }

        action(viewModel, PasswordBox.Password);

        // Clear it as soon as it has been used, so the entered password does not sit in a live
        // control while the settings panel stays open.
        PasswordBox.Clear();
    }
}
