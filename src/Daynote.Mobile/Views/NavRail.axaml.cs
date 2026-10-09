using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Daynote.Mobile.Views;

/// <summary>The two-pane layout's tab rail. All behaviour is in <see cref="ViewModels.MobileShellViewModel"/>.</summary>
public partial class NavRail : UserControl
{
    public NavRail() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
