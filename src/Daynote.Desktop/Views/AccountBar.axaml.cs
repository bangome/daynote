using Avalonia.Controls;
using Avalonia.Interactivity;
using Daynote.Desktop.ViewModels;

namespace Daynote.Desktop.Views;

/// <summary>
/// The account strip at the foot of the left column and the menu it opens.
/// </summary>
/// <remarks>
/// The rows are click handlers rather than bound commands for one reason: each has to close the
/// popover first. A light-dismiss menu left standing over the panel it just opened reads as a click
/// that did nothing. The settings button on the row itself goes the same way, and never opens the
/// popover: the inner button takes the press before the row sees it.
/// </remarks>
public partial class AccountBar : UserControl
{
    public AccountBar()
    {
        InitializeComponent();
    }

    /// <summary>Catalog strings, reached as <c>#Root.Strings</c> from inside the data templates.</summary>
    public AppStringsProxy Strings => AppStringsProxy.Instance;

    private void OnOpenAccount(object? sender, RoutedEventArgs e) => Invoke(shell => shell.OpenAccountCommand);

    private void OnOpenSettings(object? sender, RoutedEventArgs e) => Invoke(shell => shell.OpenSettingsCommand);

    private void Invoke(Func<DesktopShellViewModel, System.Windows.Input.ICommand> pick)
    {
        Toggle.Flyout?.Hide();

        if (DataContext is not DesktopShellViewModel shell)
        {
            return;
        }

        System.Windows.Input.ICommand command = pick(shell);
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}
