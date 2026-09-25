using Avalonia.Data.Converters;
using Avalonia.Media;
using Daynote.App.Localization;

namespace Daynote.Mobile.ViewModels;

/// <summary>Small value converters the phone views need, the counterpart of the desktop's AccountGlyphs.</summary>
public static class MobileConverters
{
    /// <summary>The tick inside a to-do's round checkbox, or nothing when it is open.</summary>
    public static readonly IValueConverter CheckGlyph =
        new FuncValueConverter<bool, string>(checkedOff => checkedOff ? "✓" : string.Empty);

    /// <summary>
    /// The activity dot under a calendar day. It is always laid out and only its opacity changes, so
    /// the day numbers never shift between a day with notes and one without.
    /// </summary>
    public static readonly IValueConverter DotOpacity =
        new FuncValueConverter<bool, double>(hasActivity => hasActivity ? 0.75 : 0);

    /// <summary>A filled star for a favourite, an outline for anything else.</summary>
    public static readonly IValueConverter FavoriteFill =
        new FuncValueConverter<bool, IBrush?>(favorite => favorite ? Brushes.Goldenrod : null);

    public static readonly IValueConverter UnlockMethodLabel =
        new FuncValueConverter<bool, string>(usingKey => usingKey ? AppStrings.AccountUsePassphrase : AppStrings.AccountUseRecoveryKey);

    public static readonly IValueConverter CopyLabel =
        new FuncValueConverter<bool, string>(copied => copied ? AppStrings.RecoveryKeyCopied : AppStrings.RecoveryKeyCopy);
}
