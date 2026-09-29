using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Localization;
using Daynote.Core.Sync;

namespace Daynote.App.Account;

/// <summary>
/// One local store per account, as the account panel sees it (docs/PROFILES.md §5): the <i>Move</i> /
/// <i>Keep</i> question after a sign-in, the sign-out choice, what happens to a deleted account's
/// notes, and the signal that the host has to rebuild over another profile.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these ends in <see cref="ProfileSwitchRequested"/> rather than in this view model
/// changing its own state to "signed in" or "signed out": the database under every service belongs to
/// the profile being left, so the only honest next step is for the host to flush the editor and build
/// the app again over the new folder (§8).
/// </para>
/// <para>
/// Without profiles — a single-root composition, as the tests that exercise one root build — sign-in,
/// sign-out and deletion behave exactly as they did before, and none of these surfaces appear.
/// </para>
/// </remarks>
public sealed partial class AccountViewModel
{
    /// <summary>The sign-in waiting for the <i>Move</i> / <i>Keep</i> answer; owns the session until then.</summary>
    private AccountHandOff? pendingHandOff;

    /// <summary>The question after a sign-in: bring this device's notes along, or leave them here.</summary>
    [ObservableProperty]
    private bool isChoosingHandOff;

    /// <summary>The sign-out choice is open: keep this account's notes on the device, or remove them.</summary>
    [ObservableProperty]
    private bool isChoosingSignOut;

    /// <summary>Changes that did not make it out before sign-out, as counted when the choice opened.</summary>
    [ObservableProperty]
    private int unsyncedChangeCount;

    /// <summary><i>Remove</i> was pressed with unsynced changes: the next press discards them.</summary>
    [ObservableProperty]
    private bool isConfirmingRemoveUnsynced;

    /// <summary>The account is gone on the server; its notes on this device wait for a decision.</summary>
    [ObservableProperty]
    private bool isChoosingDeletedNotes;

    /// <summary>The session was already gone when deletion was asked for (<see cref="AccountDeletion.SessionAlreadyGone"/>).</summary>
    [ObservableProperty]
    private bool wasSessionAlreadyGone;

    /// <summary>
    /// <i>Remove</i> was pressed for a deleted account's notes: the next press deletes the only copy
    /// this device has, which cannot be undone.
    /// </summary>
    [ObservableProperty]
    private bool isConfirmingRemoveDeletedNotes;

    /// <summary>A switch was requested; the host is about to rebuild the app over another profile.</summary>
    [ObservableProperty]
    private bool isSwitchingProfile;

    /// <summary>
    /// This profile's database belongs to another account than its folder or session (docs/PROFILES.md
    /// §4). Signing in as the folder's account is refused, so the panel offers the way out instead.
    /// </summary>
    [ObservableProperty]
    private bool isOwnerMismatch;

    /// <summary>
    /// Saves whatever the editor holds before a profile is left, supplied by the host. It has to run
    /// before the account operation, not after: once a deleted account's notes have been kept as local
    /// notes, or its folder marked for removal, a save into that folder would land nowhere. False
    /// means the save failed and nothing may be switched. Null (tests, a host without an editor)
    /// counts as nothing to save.
    /// </summary>
    public Func<Task<bool>>? FlushEditor { get; set; }

    /// <summary>
    /// Raised once the device points at another profile. The host flushes the editor and rebuilds
    /// over the newly active profile: a relaunch on the desktop, a new composition on a phone.
    /// </summary>
    public event EventHandler? ProfileSwitchRequested;

    /// <summary>
    /// The sign-in buttons. Hidden while a question about notes is open or a switch is under way, so
    /// the only thing to press is the answer.
    /// </summary>
    public bool ShowsSignIn =>
        IsSignedOut && !IsChoosingHandOff && !IsChoosingDeletedNotes && !IsSwitchingProfile && !IsProfileSwitchStalled;

    public string HandOffMessage
    {
        get
        {
            LocalContent content = pendingHandOff?.LocalContent ?? default;
            string notes = AppStrings.ProfileMoveBody(content.Notes);
            return content.Files > 0 ? notes + " " + AppStrings.ProfileMoveFiles(content.Files) : notes;
        }
    }

    public bool HasUnsyncedChanges => UnsyncedChangeCount > 0;

    public string UnsyncedChangesMessage => AppStrings.SignOutUnsynced(UnsyncedChangeCount);

    public string RemoveUnsyncedMessage => AppStrings.SignOutRemoveUnsynced(UnsyncedChangeCount);

    public string DeletedNotesMessage => WasSessionAlreadyGone
        ? AppStrings.DeletedNotesBodySessionGone
        : AppStrings.DeletedNotesBody;

    /// <summary>
    /// The second question before a deleted account's notes are removed. It says what the first
    /// could not: this device holds the only copy, and — if the server had already forgotten the
    /// session — the account may still exist without this device's unsynced changes.
    /// </summary>
    public string RemoveDeletedNotesMessage => WasSessionAlreadyGone
        ? AppStrings.DeletedNotesRemoveBodySessionGone
        : AppStrings.DeletedNotesRemoveBody;

    /// <summary>
    /// Makes an account profile ready before its notes are first read: on its first start the
    /// database is marked signed in and a pending <i>Move</i> is imported, so the moved notes are
    /// simply there when the day loads (docs/PROFILES.md §5.2 step 4). The host calls this ahead of
    /// loading the notes; a failure is logged and the app carries on, retried on the next start.
    /// </summary>
    public async Task PrepareProfileAsync()
    {
        try
        {
            await accounts.PrepareProfileAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine($"Preparing the account profile failed: {exception}");
        }
    }

    /// <summary>
    /// What a sign-in produced. True when it belongs in another profile, in which case either the
    /// question is now open or, with nothing to ask about, the switch has already been requested.
    /// </summary>
    private async Task<bool> HandOffIfNeededAsync(SignInResult result)
    {
        if (result.HandOff is not { } handOff)
        {
            return false;
        }

        pendingHandOff?.Dispose();
        pendingHandOff = handOff;
        if (!handOff.AsksToMove && await CompleteHandOffAsync(handOff, moveLocalNotes: false).ConfigureAwait(true))
        {
            pendingHandOff = null;
            return true;
        }

        // With something to ask about — or when the hand-off just failed, so it can be tried again
        // from the same question rather than through the browser.
        IsChoosingHandOff = true;
        OnPropertyChanged(nameof(HandOffMessage));
        return true;
    }

    [RelayCommand]
    private Task MoveLocalNotesAsync() => AnswerHandOffAsync(moveLocalNotes: true);

    [RelayCommand]
    private Task KeepLocalNotesAsync() => AnswerHandOffAsync(moveLocalNotes: false);

    private async Task AnswerHandOffAsync(bool moveLocalNotes)
    {
        if (pendingHandOff is not { } handOff)
        {
            return;
        }

        await RunAsync(async () =>
        {
            if (await CompleteHandOffAsync(handOff, moveLocalNotes).ConfigureAwait(true))
            {
                pendingHandOff = null;
                IsChoosingHandOff = false;
            }
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Saves the editor, hands the session to its account folder and asks for the switch. False when
    /// the save or the hand-off failed; the question then stays open to be answered again.
    /// </summary>
    private async Task<bool> CompleteHandOffAsync(AccountHandOff handOff, bool moveLocalNotes)
    {
        // First, so a Move takes the note being typed along with the rest.
        if (!await FlushEditorAsync().ConfigureAwait(true))
        {
            return false;
        }

        if (!await SwitchesAsync(() => accounts.CompleteHandOffAsync(handOff, moveLocalNotes).AsTask()).ConfigureAwait(true))
        {
            return false;
        }

        RequestProfileSwitch();
        return true;
    }

    /// <summary>
    /// Sign out, in an account profile (docs/PROFILES.md §5.4): sync first if anything is waiting,
    /// then open the choice with whatever still did not make it out. Without profiles, or in the local
    /// profile, it signs out at once as it always did.
    /// </summary>
    [RelayCommand]
    private async Task SignOutAsync()
    {
        await RunAsync(async () =>
        {
            // A run still on the wire would write the old account's cursor back after sign-out
            // cleared it, and the next account's first pull would start from there.
            await syncInFlight.ConfigureAwait(true);
            if (!accounts.IsAccountProfile)
            {
                await accounts.SignOutAsync().ConfigureAwait(true);
                ResetToSignedOut();
                return;
            }

            // The editor first: text autosave has not written yet is not in the outbox, so neither
            // the sync below nor the count after it would know about it.
            if (!await FlushEditorAsync().ConfigureAwait(true))
            {
                return;
            }

            if (await accounts.CountPendingChangesAsync().ConfigureAwait(true) > 0 && !IsLocked && !IsKeyMissing)
            {
                // Quietly: being offline here is exactly what the count below is for.
                await RunSyncAsync(userInitiated: false).ConfigureAwait(true);
                ErrorMessage = null;
            }

            UnsyncedChangeCount = await accounts.CountPendingChangesAsync().ConfigureAwait(true);
            IsConfirmingRemoveUnsynced = false;
            IsChoosingSignOut = true;
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private Task SignOutKeepAsync() => RunAsync(() => FinishSignOutAsync(removeFromDevice: false));

    /// <summary>
    /// <i>Remove</i>. With changes that never reached the server, the first press only asks again:
    /// removing them loses them everywhere, which deserves a second, explicit yes.
    /// </summary>
    /// <remarks>
    /// The count shown when the choice opened is not trusted here. The editor stays usable beside the
    /// card, so the note being typed is saved and the outbox counted again at the moment of the press;
    /// if more is waiting than the user last agreed to lose, they are asked again with the new number.
    /// </remarks>
    [RelayCommand]
    private Task SignOutRemoveAsync() => RunAsync(async () =>
    {
        if (!await FlushEditorAsync().ConfigureAwait(true))
        {
            return;
        }

        int pending = await accounts.CountPendingChangesAsync().ConfigureAwait(true);
        if (pending > 0 && (!IsConfirmingRemoveUnsynced || pending > UnsyncedChangeCount))
        {
            UnsyncedChangeCount = pending;
            IsConfirmingRemoveUnsynced = true;
            return;
        }

        await FinishSignOutAsync(removeFromDevice: true).ConfigureAwait(true);
    });

    [RelayCommand]
    private void CancelSignOut()
    {
        IsChoosingSignOut = false;
        IsConfirmingRemoveUnsynced = false;
    }

    /// <summary>
    /// The way out of a profile whose database belongs to someone else (docs/PROFILES.md §4): the
    /// session is dropped and the device goes back to its local notes. The folder is kept — whose
    /// notes are in it is not something to guess — and nothing in it syncs.
    /// </summary>
    [RelayCommand]
    private Task LeaveMismatchedProfileAsync() => RunAsync(() => FinishSignOutAsync(removeFromDevice: false));

    /// <summary>Saves the editor, signs out and asks for the switch. Runs inside <see cref="RunAsync"/>.</summary>
    private async Task FinishSignOutAsync(bool removeFromDevice)
    {
        await syncInFlight.ConfigureAwait(true);
        if (!await FlushEditorAsync().ConfigureAwait(true))
        {
            return;
        }

        bool switches = false;
        if (!await SwitchesAsync(async () => switches = await accounts.SignOutAsync(removeFromDevice).ConfigureAwait(true))
            .ConfigureAwait(true))
        {
            return;
        }

        IsChoosingSignOut = false;
        IsConfirmingRemoveUnsynced = false;
        IsOwnerMismatch = false;
        ResetToSignedOut();
        if (switches)
        {
            RequestProfileSwitch();
        }
    }

    [RelayCommand]
    private Task KeepDeletedNotesAsync() => FinishDeletionAsync(keepNotesAsLocal: true);

    /// <summary>
    /// <i>Remove</i> a deleted account's notes. After the server delete this device holds the only
    /// copy, so the first press only asks again, saying that it cannot be undone.
    /// </summary>
    [RelayCommand]
    private Task RemoveDeletedNotesAsync()
    {
        if (!IsConfirmingRemoveDeletedNotes)
        {
            IsConfirmingRemoveDeletedNotes = true;
            return Task.CompletedTask;
        }

        return FinishDeletionAsync(keepNotesAsLocal: false);
    }

    [RelayCommand]
    private void CancelRemoveDeletedNotes() => IsConfirmingRemoveDeletedNotes = false;

    private async Task FinishDeletionAsync(bool keepNotesAsLocal)
    {
        await RunAsync(async () =>
        {
            // Before the notes are copied out: the note being typed is one of them.
            if (!await FlushEditorAsync().ConfigureAwait(true))
            {
                return;
            }

            bool switches = false;
            if (!await SwitchesAsync(async () => switches = await accounts.FinishDeletionAsync(keepNotesAsLocal).ConfigureAwait(true))
                .ConfigureAwait(true))
            {
                return;
            }

            IsChoosingDeletedNotes = false;
            IsConfirmingRemoveDeletedNotes = false;
            if (switches)
            {
                RequestProfileSwitch();
            }
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Called by the host when it could not carry out a requested switch — the relaunch was refused
    /// because the editor would not save. <c>profile.json</c> already names the new profile, so the
    /// next start opens it; until then this panel says so and offers to try again.
    /// </summary>
    public void NotifyProfileSwitchFailed()
    {
        IsSwitchingProfile = false;
        IsProfileSwitchStalled = true;
        ErrorMessage = AppStrings.ProfileSwitchStalled;
    }

    /// <summary>True after <see cref="NotifyProfileSwitchFailed"/>: the device points at a profile this process is not running.</summary>
    [ObservableProperty]
    private bool isProfileSwitchStalled;

    [RelayCommand]
    private void RetryProfileSwitch()
    {
        IsProfileSwitchStalled = false;
        ErrorMessage = null;
        RequestProfileSwitch();
    }

    /// <summary>True when the host's editor saved, or there is no editor to save.</summary>
    private async Task<bool> FlushEditorAsync()
    {
        if (FlushEditor is not { } flush || await flush().ConfigureAwait(true))
        {
            return true;
        }

        ErrorMessage = AppStrings.ProfileErrorUnsaved;
        return false;
    }

    /// <summary>
    /// Runs a step that moves files between profiles. Whatever refuses — a full or read-only disk, a
    /// folder held open, a database that is locked, a keystore that will not seal — is not an account
    /// failure, so it gets its own sentence instead of reaching the command as an unhandled fault; the
    /// device stays on the profile it was on.
    /// </summary>
    private async Task<bool> SwitchesAsync(Func<Task> step)
    {
        try
        {
            await step().ConfigureAwait(true);
            return true;
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or AccountException))
        {
            System.Diagnostics.Debug.WriteLine($"Switching profiles failed: {exception}");
            ErrorMessage = AppStrings.AccountErrorProfile;
            return false;
        }
    }

    private void RequestProfileSwitch()
    {
        IsSwitchingProfile = true;
        ProfileSwitchRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Re-raises the sentences that carry a count, for a language switch.</summary>
    private void RefreshProfileMessages()
    {
        OnPropertyChanged(nameof(HandOffMessage));
        OnPropertyChanged(nameof(UnsyncedChangesMessage));
        OnPropertyChanged(nameof(RemoveUnsyncedMessage));
        OnPropertyChanged(nameof(DeletedNotesMessage));
        OnPropertyChanged(nameof(RemoveDeletedNotesMessage));
    }

    partial void OnIsChoosingHandOffChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(ShowsSignIn));
    }

    partial void OnIsChoosingDeletedNotesChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(ShowsSignIn));
    }

    partial void OnIsSwitchingProfileChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(ShowsSignIn));
    }

    partial void OnIsProfileSwitchStalledChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(ShowsSignIn));
    }

    partial void OnUnsyncedChangeCountChanged(int value)
    {
        _ = value;
        OnPropertyChanged(nameof(HasUnsyncedChanges));
        OnPropertyChanged(nameof(UnsyncedChangesMessage));
        OnPropertyChanged(nameof(RemoveUnsyncedMessage));
    }

    partial void OnWasSessionAlreadyGoneChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(DeletedNotesMessage));
        OnPropertyChanged(nameof(RemoveDeletedNotesMessage));
    }
}
