using Daynote.App.Localization;

namespace Daynote.App.Shell.Product;

/// <summary>
/// Language handling for the product shell, split out of the main partial to keep that file under
/// the source-size budget enforced by <c>SourceStructureTests</c>.
/// </summary>
public sealed partial class ProductShellViewModel : ILanguageAware
{
    /// <summary>
    /// Re-derives the header text this view model stores (the day label and note count) and
    /// invalidates the rest of its bindings. The calendar, to-do, clipboard, and settings panels
    /// observe <see cref="LocalizationService"/> themselves, so each refreshes its own surface.
    /// </summary>
    void ILanguageAware.OnLanguageChanged()
    {
        RefreshHeader();
        OnPropertyChanged(string.Empty);
    }
}
