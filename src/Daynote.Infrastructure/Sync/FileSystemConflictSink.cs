using System.Text;
using Daynote.Core.Sync;

namespace Daynote.Infrastructure.Sync;

/// <summary>
/// Writes note versions that last-write-wins discarded to
/// <c>%LocalAppData%\Daynote\conflicts\</c> as plain text.
/// </summary>
/// <remarks>
/// This is what makes last-write-wins acceptable (docs/CLOUD_SYNC.md §7.4). Plain text, not the
/// database: the user has to be able to find and read these without the app's help, possibly on a
/// day the app is not cooperating.
/// </remarks>
public sealed class FileSystemConflictSink : ISyncConflictSink
{
    internal const string DirectoryName = "conflicts";

    private readonly string directory;

    public FileSystemConflictSink(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        directory = Path.Combine(Path.GetFullPath(dataRoot), DirectoryName);
    }

    public async ValueTask SaveAsync(
        IReadOnlyList<DisplacedNote> displaced,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(displaced);
        if (displaced.Count == 0)
        {
            return;
        }

        Directory.CreateDirectory(directory);
        foreach (DisplacedNote note in displaced)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The timestamp goes in the name so two conflicts on one note do not overwrite each other,
            // and so the user can tell which version they are looking at.
            string stamp = note.UpdatedUtc.ToUniversalTime().ToString(
                "yyyyMMdd-HHmmss",
                System.Globalization.CultureInfo.InvariantCulture);
            string fileName = $"{note.LocalDate}-{stamp}-{note.Id[..8]}.txt";

            var text = new StringBuilder()
                .AppendLine($"Daynote — a version of this note was replaced by a newer one from another device.")
                .AppendLine($"Date:  {note.LocalDate}")
                .AppendLine($"Title: {note.Title}")
                .AppendLine($"Saved: {note.UpdatedUtc.ToUniversalTime():u}")
                .AppendLine()
                .Append(note.Body);

            await File.WriteAllTextAsync(
                Path.Combine(directory, fileName),
                text.ToString(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
                cancellationToken).ConfigureAwait(false);
        }
    }
}
