namespace Daynote.Core.Files;

/// <summary>
/// Bounds and format policy for user-attached day files. The size cap is a hard limit enforced while
/// streaming into the content-addressed store so an oversize payload never leaves a partial asset or row.
/// </summary>
public static class FileCapturePolicy
{
    public const long MaxFileBytes = 256L * 1024 * 1024;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif",
    };

    public static bool IsImageName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return false;
        }

        string extension = Path.GetExtension(displayName);
        return extension.Length != 0 && ImageExtensions.Contains(extension);
    }

    public static string NormalizeExtension(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return string.Empty;
        }

        string extension = Path.GetExtension(displayName);
        return extension.Length == 0 ? string.Empty : extension.ToLowerInvariant();
    }
}
