namespace Daynote.App.Shell.Product;

/// <summary>
/// Applies the product Light/Dark theme to the running application. Abstracted so the shell view model
/// can persist and drive theme without a live WPF <c>Application</c> (tests inject a no-op).
/// </summary>
public interface IThemeApplier
{
    void Apply(bool dark);
}
