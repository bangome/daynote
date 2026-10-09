using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Daynote.Mobile.Views;

/// <summary>The tablet's sidebar. All behaviour is in <see cref="ViewModels.MobileShellViewModel"/>.</summary>
public partial class TabletSidebar : UserControl
{
    public TabletSidebar() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
