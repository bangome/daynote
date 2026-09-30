using System.ComponentModel;

namespace Daynote.App.Shell.Product;

/// <summary>
/// Keeping the sidebar's account row in step with the account.
/// </summary>
/// <remarks>
/// <see cref="ProductShellViewModel.Account"/> is derived from the settings view model, and the row's
/// four lines are derived from that, so none of them raises anything on its own: signing in changed
/// the account and the row went on saying "sign in". The shell follows two notifications here — the
/// settings view model arriving from composition, and the account's own changes — and re-raises the
/// row from both. The Avalonia shell does the same in DesktopShellViewModel.
/// </remarks>
public sealed partial class ProductShellViewModel
{
    private Account.AccountViewModel? _observedAccount;

    partial void OnSettingsViewModelChanged(Settings.SettingsViewModel? value)
    {
        if (_observedAccount is not null)
        {
            _observedAccount.PropertyChanged -= OnAccountChanged;
        }

        _observedAccount = value?.Account;
        if (_observedAccount is not null)
        {
            _observedAccount.PropertyChanged += OnAccountChanged;
        }

        RefreshAccountBar();
    }

    private void OnAccountChanged(object? sender, PropertyChangedEventArgs e) => RefreshAccountBar();

    /// <summary>Everything the sidebar's account row and the account card read off the account.</summary>
    private void RefreshAccountBar()
    {
        OnPropertyChanged(nameof(Account));
        OnPropertyChanged(nameof(AccountInitial));
        OnPropertyChanged(nameof(AccountRowSubtitle));
        OnPropertyChanged(nameof(AccountCardTitle));
        OnPropertyChanged(nameof(AccountCardSubtitle));
        OnPropertyChanged(nameof(AccountCardAction));
    }
}
