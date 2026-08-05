using System.Collections.ObjectModel;
using Daynote.Core.Domain.Notes;

namespace Daynote.Core.Notes;

public sealed class DayWorkspace
{
    private readonly IReadOnlyDictionary<NoteId, int> _revisions;

    public DayWorkspace(NoteSet notes, IEnumerable<KeyValuePair<NoteId, int>> revisions)
    {
        Notes = notes ?? throw new ArgumentNullException(nameof(notes));
        ArgumentNullException.ThrowIfNull(revisions);
        _revisions = new ReadOnlyDictionary<NoteId, int>(revisions.ToDictionary());
    }

    public NoteSet Notes { get; }

    public int RevisionOf(NoteId id) => _revisions.TryGetValue(id, out int revision)
        ? revision
        : throw new ArgumentException("The note is not persisted in this workspace.", nameof(id));
}
