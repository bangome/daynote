using Avalonia;
using Avalonia.Styling;
using Daynote.App.Shell.Product;

namespace Daynote.Desktop.Platform;

/// <summary>Flips the application's theme variant; the palette dictionaries do the rest.</summary>
public sealed class AvaloniaThemeApplier : IThemeApplier
{
    private readonly Application _application;

    public AvaloniaThemeApplier(Application application)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
    }

    public void Apply(bool dark) =>
        _application.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
}
