using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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

    /// <summary>"요금제 비교": scrolls the 계정 page down to the plan table.</summary>
    private void OnComparePlans(object? sender, RoutedEventArgs e) =>
        this.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.Name == "PlanTable")?.BringIntoView();
}
