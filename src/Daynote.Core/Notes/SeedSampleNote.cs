using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Settings;

namespace Daynote.Core.Notes;

/// <summary>
/// First-run helper that seeds a single sample note on the given date so a brand-new user sees the app
/// in use (checkbox to-dos, due stamps, sections). Runs once — a persisted flag stops later launches —
/// and only when that date has no notes yet, so it never disturbs existing data. The localized
/// title/body are supplied by the caller to keep Core free of UI strings.
///
/// Because the note is created in the first-run language, <see cref="RelocalizeAsync"/> can rewrite it
/// when the user switches languages — but only while it is still the untouched sample (its current body
/// equals the last body we wrote), so a user's edits are never overwritten.
/// </summary>
public sealed class SeedSampleNote
{
    private readonly INoteRepository _repository;
    private readonly ISettingsStore _settings;
    private readonly Func<NoteId> _nextId;

    public SeedSampleNote(INoteRepository repository, ISettingsStore settings, Func<NoteId> nextId)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _nextId = nextId ?? throw new ArgumentNullException(nameof(nextId));
    }

    /// <summary>Returns true when a sample note was actually created.</summary>
    public async Task<bool> ExecuteAsync(LocalDate today, string title, string body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(body);
        if (await _settings.GetBoolAsync(OnboardingSettings.SampleSeededKey, false, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        DayWorkspace workspace = await _repository.GetDayWorkspaceStateAsync(today, cancellationToken).ConfigureAwait(false);
        bool created = false;
        if (workspace.Notes.IsProjectionOnly)
        {
            NoteId id = _nextId();
            await _repository.SaveNoteAsync(
                new NoteSaveRequest(id, today, title, body, Revision: 0, IsNew: true, HasCustomTitle: true),
                cancellationToken).ConfigureAwait(false);
            await _settings.SetAsync(OnboardingSettings.SampleNoteIdKey, id.Value.ToString(), cancellationToken).ConfigureAwait(false);
            await _settings.SetAsync(OnboardingSettings.SampleNoteDateKey, today.ToString(), cancellationToken).ConfigureAwait(false);
            await _settings.SetAsync(OnboardingSettings.SampleNoteBodyKey, body, cancellationToken).ConfigureAwait(false);
            created = true;
        }

        await _settings.SetBoolAsync(OnboardingSettings.SampleSeededKey, true, cancellationToken).ConfigureAwait(false);
        return created;
    }

    /// <summary>
    /// Rewrites the seeded sample note into the current language (title + body from the caller) while it
    /// is untouched. <paramref name="bodyFor"/> builds the body for the sample's own date (so dated
    /// to-dos keep that date). Returns the note's date when it was rewritten, otherwise null.
    /// </summary>
    public async Task<LocalDate?> RelocalizeAsync(string title, Func<LocalDate, string> bodyFor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(bodyFor);

        string? idText = await _settings.GetAsync(OnboardingSettings.SampleNoteIdKey, cancellationToken).ConfigureAwait(false);
        string? dateText = await _settings.GetAsync(OnboardingSettings.SampleNoteDateKey, cancellationToken).ConfigureAwait(false);
        string? seededBody = await _settings.GetAsync(OnboardingSettings.SampleNoteBodyKey, cancellationToken).ConfigureAwait(false);
        if (idText is null || dateText is null || seededBody is null
            || !Guid.TryParse(idText, out Guid guid))
        {
            return null;
        }

        DomainResult<NoteId> id = NoteId.Create(guid);
        DomainResult<LocalDate> date = LocalDate.Parse(dateText);
        if (!id.IsSuccess || !date.IsSuccess)
        {
            return null;
        }

        DayWorkspace workspace = await _repository.GetDayWorkspaceStateAsync(date.Value, cancellationToken).ConfigureAwait(false);
        Note? note = null;
        foreach (Note candidate in workspace.Notes.Notes)
        {
            if (!candidate.IsProjection && candidate.Id is { } noteId && noteId == id.Value)
            {
                note = candidate;
                break;
            }
        }

        // Gone (deleted) or edited by the user → leave it alone.
        if (note is null || !string.Equals(note.Body, seededBody, StringComparison.Ordinal))
        {
            return null;
        }

        string newBody = bodyFor(date.Value);
        await _repository.SaveNoteAsync(
            new NoteSaveRequest(id.Value, date.Value, title, newBody, workspace.RevisionOf(id.Value), IsNew: false, HasCustomTitle: true),
            cancellationToken).ConfigureAwait(false);
        await _settings.SetAsync(OnboardingSettings.SampleNoteBodyKey, newBody, cancellationToken).ConfigureAwait(false);
        return date.Value;
    }
}
