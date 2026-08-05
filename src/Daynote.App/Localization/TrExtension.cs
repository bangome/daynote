using System.Windows.Markup;

namespace Daynote.App.Localization;

/// <summary>
/// Binds a XAML property to one localized string: <c>Text="{loc:Tr SettingsTitle}"</c>.
/// </summary>
/// <remarks>
/// This replaces the <c>{x:Static loc:AppStrings.Key}</c> form the shell used while Korean was the
/// only language. <c>x:Static</c> resolves once when the XAML loads, so switching languages would
/// have left every already-loaded window in the old language. Producing a real binding against
/// <see cref="LocalizationService"/>'s indexer instead means the switch is live: the service raises
/// a change notification for the indexer and WPF re-reads every one of these.
/// </remarks>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key) => Key = key;

    /// <summary>The catalog key — the same name as the <see cref="AppStrings"/> member.</summary>
    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object? ProvideValue(IServiceProvider serviceProvider)
    {
        // Fully qualified: WinForms (pulled in by the tray icon) has its own Binding type.
        var binding = new System.Windows.Data.Binding($"[{Key}]")
        {
            Source = LocalizationService.Instance,
            Mode = System.Windows.Data.BindingMode.OneWay,
        };

        return binding.ProvideValue(serviceProvider);
    }
}
