using Avalonia;
using Avalonia.Styling;
using Daynote.App.Shell.Product;

namespace Daynote.Mobile.Platform;

/// <summary>
/// Switches the app's theme variant, the phone's copy of the desktop <c>AvaloniaThemeApplier</c>.
/// </summary>
/// <remarks>
/// The system bar colours follow from this on both platforms: Android reads the window background,
/// and iOS the <c>UIUserInterfaceStyle</c> Avalonia sets alongside the variant.
/// </remarks>
public sealed class MobileThemeApplier(Application application) : IThemeApplier
{
    private readonly Application _application = application ?? throw new ArgumentNullException(nameof(application));

    public void Apply(bool dark) =>
        _application.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
}
