using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Localization;
using Daynote.Core.Settings;

namespace Daynote.Mobile.ViewModels;

/// <summary>
/// The settings page's rows, the account page behind its card, and the search page's recent terms.
/// </summary>
public sealed partial class MobileShellViewModel
{
    /// <summary>Where the recent search terms are kept, one per line, newest first.</summary>
    private const string RecentSearchesKey = "mobile.search.recent";

    private const int RecentSearchLimit = 6;

    private string _lastQuery = string.Empty;

    // ── The account page ─────────────────────────────────────────────────────────────────────────
    // The settings page shows the account as one card; everything the account can do - sign in, the
    // lock, the questions a sign-in or sign-out asks, deleting it - is one tap further in, on a page
    // of its own that covers the tabs the way the editor does.

    [ObservableProperty]
    private bool _isAccountOpen;

    [RelayCommand]
    private void OpenAccount()
    {
        if (Account is null)
        {
            return;
        }

        IsAccountOpen = true;
        if (Account is { IsSignedIn: true } account)
        {
            _ = account.RefreshBillingCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private void CloseAccount() => IsAccountOpen = false;

    /// <summary>"2026년" / "2026": the month sheet's year.</summary>
    public string PickerYearText => MobileStrings.Format("MobileYearFormat", PickerYear.ToString(System.Globalization.CultureInfo.InvariantCulture));

    partial void OnPickerYearChanged(int value) => OnPropertyChanged(nameof(PickerYearText));

    /// <summary>The letter in the card's avatar.</summary>
    public string AccountInitial => Account is { IsSignedIn: true } account ? account.AvatarInitial : "D";

    /// <summary>"name · Pro" when signed in.</summary>
    public string AccountCardTitle => Account is { IsSignedIn: true } account
        ? string.Concat(account.DisplayName, " · ", account.PlanBadge)
        : Account is null ? MobileStrings.Get("AccountBarLocalMobile") : AccountBarTitle;

    /// <summary>The sync state and the note count, or what signing in is for.</summary>
    public string AccountCardSubtitle
    {
        get
        {
            if (Account is null)
            {
                return MobileCatalog.NoteCount(TotalNoteCount);
            }

            if (NeedsAccountAttention)
            {
                return MobileStrings.Get("MobileAccountAttention");
            }

            if (!IsSignedIn)
            {
                return MobileStrings.Get("MobileAccountSignInSubtitle");
            }

            string count = MobileCatalog.NoteCount(TotalNoteCount);
            return AccountBarSubtitle.Length > 0 ? string.Concat(AccountBarSubtitle, " · ", count) : count;
        }
    }

    /// <summary>
    /// A question is waiting on the account page - the hand-off after a sign-in, what to do with a
    /// deleted account's notes, a stalled profile switch, a locked or keyless device - so the card
    /// says so rather than leaving it one tap away unseen.
    /// </summary>
    public bool NeedsAccountAttention => Account is { } account
        && (account.IsChoosingHandOff || account.IsChoosingDeletedNotes || account.IsProfileSwitchStalled
            || account.IsLocked || account.IsKeyMissing);

    partial void OnTotalNoteCountChanged(int value) => OnPropertyChanged(nameof(AccountCardSubtitle));

    private void RefreshAccountCard()
    {
        OnPropertyChanged(nameof(AccountInitial));
        OnPropertyChanged(nameof(AccountCardTitle));
        OnPropertyChanged(nameof(AccountCardSubtitle));
        OnPropertyChanged(nameof(NeedsAccountAttention));
        OnPropertyChanged(nameof(StorageText));
        OnPropertyChanged(nameof(SyncNowLabel));
    }

    // ── Theme and language ───────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void UseLightTheme() => IsDark = false;

    [RelayCommand]
    private void UseDarkTheme() => IsDark = true;

    public bool IsKorean => LocalizationService.Instance.Language == AppLanguage.Korean;

    public bool IsEnglish => LocalizationService.Instance.Language == AppLanguage.English;

    /// <summary>Switches the language at once and keeps it, under the key the desktop uses.</summary>
    [RelayCommand]
    private async Task SelectLanguage(AppLanguage language)
    {
        if (LocalizationService.Instance.Language == language)
        {
            return;
        }

        LocalizationService.Instance.SetLanguage(language);
        await _settings.SetAsync(UiSettings.LanguageKey, AppLanguages.ToTag(language)).ConfigureAwait(true);
    }

    // ── Data ─────────────────────────────────────────────────────────────────────────────────────

    public string StorageText => MobileStrings.Get(IsSignedIn ? "MobileStorageDeviceCloud" : "MobileStorageDevice");

    public string SyncNowLabel => Account is { IsBusy: true } ? AppStrings.SyncChipSyncing : MobileStrings.Get("MobileSyncRun");

    /// <summary>Syncs when signed in; otherwise opens the account page, where signing in is.</summary>
    [RelayCommand]
    private Task SyncNow()
    {
        if (Account is { IsSignedIn: true } account)
        {
            return account.SyncCommand.CanExecute(null) ? account.SyncCommand.ExecuteAsync(null) : Task.CompletedTask;
        }

        OpenAccount();
        return Task.CompletedTask;
    }

    /// <summary>"데이노트 1.5.0", the build a bug report can name.</summary>
    public string VersionText
    {
        get
        {
            string? informational = typeof(MobileShellViewModel).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            string version = informational is { Length: > 0 }
                ? informational.Split('+')[0]
                : typeof(MobileShellViewModel).Assembly.GetName().Version?.ToString(3) ?? "?";
            return MobileStrings.Format("MobileVersionFormat", version);
        }
    }

    // ── Recent searches ──────────────────────────────────────────────────────────────────────────

    /// <summary>What was searched for and then opened, newest first.</summary>
    public ObservableCollection<string> RecentSearches { get; } = [];

    public bool HasRecentSearches => RecentSearches.Count > 0;

    public bool HasSearchQuery => !string.IsNullOrWhiteSpace(Search.Query);

    public string SearchResultCountText => MobileCatalog.ResultCount(Search.Results.Count);

    /// <summary>A recent term or a tag, put in the box as if typed.</summary>
    [RelayCommand]
    private void UseSearchTerm(string? term)
    {
        if (!string.IsNullOrWhiteSpace(term))
        {
            Search.Query = term.TrimStart('#');
        }
    }

    private void OnSearchPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Search.Query))
        {
            if (!string.IsNullOrWhiteSpace(Search.Query))
            {
                _lastQuery = Search.Query.Trim();
            }

            OnPropertyChanged(nameof(HasSearchQuery));
        }
    }

    /// <summary>A search that led somewhere is worth offering again.</summary>
    private void RememberSearch()
    {
        string term = _lastQuery;
        if (term.Length == 0)
        {
            return;
        }

        RecentSearches.Remove(term);
        RecentSearches.Insert(0, term);
        while (RecentSearches.Count > RecentSearchLimit)
        {
            RecentSearches.RemoveAt(RecentSearches.Count - 1);
        }

        _ = _settings.SetAsync(RecentSearchesKey, string.Join('\n', RecentSearches));
    }

    private async Task LoadRecentSearchesAsync(CancellationToken cancellationToken)
    {
        string? stored = await _settings.GetAsync(RecentSearchesKey, cancellationToken).ConfigureAwait(true);
        RecentSearches.Clear();
        foreach (string term in (stored ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(RecentSearchLimit))
        {
            RecentSearches.Add(term);
        }
    }
}
