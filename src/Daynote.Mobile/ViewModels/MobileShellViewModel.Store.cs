using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Daynote.Mobile.ViewModels;

/// <summary>
/// The plans page, on the iPhone only: a layer over the account page the way the account page is a
/// layer over the tabs, opened from the plans row on it (docs/CLOUD_SYNC.md §14.8).
/// </summary>
public sealed partial class MobileShellViewModel
{
    /// <summary>
    /// In-App Purchase, or null on a head that sells nothing (Android) and in a build with no
    /// account. Set by composition.
    /// </summary>
    public MobileStoreViewModel? Store
    {
        get => _store;
        init
        {
            _store = value;
            if (value is not null)
            {
                value.RequestSignIn = SignInFromStore;
            }
        }
    }

    private readonly MobileStoreViewModel? _store;

    public bool HasStore => Store is not null;

    [ObservableProperty]
    private bool _isStoreOpen;

    partial void OnIsStoreOpenChanged(bool value) => OnPropertyChanged(nameof(ShowDock));

    /// <summary>The plans row's line: the plan in force, or what there is to see.</summary>
    public string StoreEntrySubtitle => Account is { IsSignedIn: true } account && account.Entitlement.HasSubscribed
        ? account.PlanBadge
        : MobileStrings.Get("StoreEntryFree");

    [RelayCommand]
    private void OpenStore()
    {
        if (Store is null)
        {
            return;
        }

        IsStoreOpen = true;
        _ = RunQuietlyAsync(Store.OpenAsync);
    }

    [RelayCommand]
    private void CloseStore() => IsStoreOpen = false;

    /// <summary>The plans page's "로그인하기": back to the account page, where signing in is.</summary>
    [RelayCommand]
    private void SignInFromStore()
    {
        IsStoreOpen = false;
        OpenAccount();
    }
}
