using Avalonia.Data.Converters;
using Daynote.App.Localization;

namespace Daynote.Desktop.ViewModels;

/// <summary>Bool-to-label converters for the account panel's two-state buttons.</summary>
public static class AccountGlyphs
{
    public static readonly IValueConverter UnlockMethodLabel =
        new FuncValueConverter<bool, string>(usingKey => usingKey ? AppStrings.AccountUsePassphrase : AppStrings.AccountUseRecoveryKey);

    public static readonly IValueConverter LockStateLabel =
        new FuncValueConverter<bool, string>(enabled => enabled ? AppStrings.AccountLockOn : AppStrings.AccountLockOff);

    public static readonly IValueConverter CopyLabel =
        new FuncValueConverter<bool, string>(copied => copied ? AppStrings.RecoveryKeyCopied : AppStrings.RecoveryKeyCopy);
}
