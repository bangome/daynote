namespace Daynote.Infrastructure.Persistence.Profiles;

/// <summary>What <see cref="ProfileStore.MigrateLegacyLayout"/> did on this start.</summary>
public enum ProfileMigrationOutcome
{
    /// <summary>
    /// <c>accounts/</c> already exists, so this base root is past the one-time migration. Nothing was
    /// read or written.
    /// </summary>
    AlreadyMigrated,

    /// <summary>
    /// The base database was not signed in (or there was none), so it stays the local profile.
    /// <c>profile.json</c> was written as <c>local</c> if it was missing; nothing moved.
    /// </summary>
    StayedLocal,

    /// <summary>
    /// The base database was signed in; its data now lives in <c>accounts/&lt;userId&gt;/</c>, a fresh local
    /// database carries the device settings, and <c>profile.json</c> points at the account.
    /// </summary>
    MovedToAccount,

    /// <summary>
    /// Something failed part-way. The base root was put back as it was, so the app keeps running on
    /// the legacy layout exactly as before this version, and the next start tries again.
    /// </summary>
    Failed,
}

/// <param name="Outcome">What happened.</param>
/// <param name="UserId">The account the data moved to, for <see cref="ProfileMigrationOutcome.MovedToAccount"/>.</param>
/// <param name="Error">Why it failed, for <see cref="ProfileMigrationOutcome.Failed"/>.</param>
public sealed record ProfileMigrationResult(
    ProfileMigrationOutcome Outcome,
    string? UserId = null,
    Exception? Error = null);
