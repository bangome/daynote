using Daynote.Core.Domain;

namespace Daynote.Core.Files;

/// <summary>
/// A user-attached file for a given local date. The bytes live in the content-addressed file store;
/// this record is the metadata projection. <see cref="IsImage"/> is derived from the display name so the
/// UI can render a preview straight from the stored asset without a separate thumbnail.
/// </summary>
public sealed record DayFile(
    Guid Id,
    LocalDate LocalDate,
    string DisplayName,
    long ByteLength,
    string AssetHash,
    string RelativePath,
    DateTimeOffset CreatedUtc,
    bool IsAvailable = false)
{
    public bool IsImage => FileCapturePolicy.IsImageName(DisplayName);
}

public sealed record DayFileDeleteResult(bool Deleted, string? ReleasedAssetPath);

public readonly record struct DayFileDeleteReceipt(bool Deleted, bool CleanupPending);
