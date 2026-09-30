using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Daynote.Mobile.ViewModels;

namespace Daynote.Mobile.Views;

/// <summary>One of the phone's pages. All behaviour is in <see cref="ViewModels.MobileShellViewModel"/>.</summary>
public partial class DayPage : UserControl
{
    public DayPage() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>A long press on a file row: the same menu as its dots.</summary>
    private void OnFileRowContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if ((sender as Control)?.DataContext is MobileFileRowViewModel row)
        {
            row.ShowMenuCommand.Execute(null);
            e.Handled = true;
        }
    }
}
