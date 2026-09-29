using Avalonia.Controls;
using Daynote.Desktop.ViewModels;

namespace Daynote.Desktop.Views;

/// <summary>The settings dialog. All behaviour is the view models'; this only hosts the markup.</summary>
public partial class SettingsPanel : UserControl
{
    public SettingsPanel()
    {
        InitializeComponent();
    }

    /// <summary>Catalog strings, reached as <c>#Root.Strings</c> from inside the settings data context.</summary>
    public AppStringsProxy Strings => AppStringsProxy.Instance;
}
