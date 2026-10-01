using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Daynote.Mobile.ViewModels;

namespace Daynote.Mobile.Views;

/// <summary>The iPhone's plans page; everything it does is on <see cref="MobileStoreViewModel"/>.</summary>
public partial class StorePage : UserControl
{
    public StorePage() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Catalog strings, reached as <c>$parent[views:StorePage].Strings</c> from inside the templates.</summary>
    public MobileStrings Strings => MobileStrings.Instance;
}
