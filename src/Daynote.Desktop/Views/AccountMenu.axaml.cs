using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Daynote.Desktop.ViewModels;

namespace Daynote.Desktop.Views;

/// <summary>
/// The titlebar account button and its menu. Its own DataContext is the account, but the two menu
/// actions belong to the shell, so they are raised through the window rather than bound — the same
/// reason the WPF control uses click handlers here.
/// </summary>
public partial class AccountMenu : UserControl
{
    public AccountMenu()
    {
        InitializeComponent();
    }

    public AppStringsProxy Strings => AppStringsProxy.Instance;

    private void OnOpenAccount(object? sender, RoutedEventArgs e) => Invoke(shell => shell.OpenAccountCommand);

    private void OnOpenSettings(object? sender, RoutedEventArgs e) => Invoke(shell => shell.OpenSettingsCommand);

    private void Invoke(Func<DesktopShellViewModel, System.Windows.Input.ICommand> pick)
    {
        // Closing first: the menu is a light-dismiss flyout, and leaving it open over the panel it
        // just opened is the sort of thing that looks like the click did nothing.
        Toggle.Flyout?.Hide();

        if (this.FindAncestorOfType<Window>()?.DataContext is DesktopShellViewModel shell)
        {
            System.Windows.Input.ICommand command = pick(shell);
            if (command.CanExecute(null))
            {
                command.Execute(null);
            }
        }
    }
}
