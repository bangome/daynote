using Avalonia.Data.Converters;
using Daynote.App.Localization;

namespace Daynote.Mobile.ViewModels;

/// <summary>Small value converters the phone views need, the counterpart of the desktop's AccountGlyphs.</summary>
public static class MobileConverters
{
    /// <summary>"9/30" as "09/30", the way every date label on the phone reads.</summary>
    public static readonly IValueConverter PaddedDate =
        new FuncValueConverter<string?, string>(label =>
            label?.Split('/') is [{ } month, { } day] && int.TryParse(month, out int m) && int.TryParse(day, out int d)
                ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{m:00}/{d:00}")
                : label ?? string.Empty);

    public static readonly IValueConverter UnlockMethodLabel =
        new FuncValueConverter<bool, string>(usingKey => usingKey ? AppStrings.AccountUsePassphrase : AppStrings.AccountUseRecoveryKey);

    public static readonly IValueConverter CopyLabel =
        new FuncValueConverter<bool, string>(copied => copied ? AppStrings.RecoveryKeyCopied : AppStrings.RecoveryKeyCopy);
}
