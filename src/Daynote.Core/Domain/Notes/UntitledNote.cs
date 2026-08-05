using System.Globalization;

namespace Daynote.Core.Domain.Notes;

/// <summary>
/// The display title shown for a note the user never named ("노트 1" / "Note 1").
/// </summary>
/// <remarks>
/// A settable format rather than an injected service: <see cref="Note.Title"/> is a computed
/// property on a domain object constructed in bulk (a whole month of summaries at a time, straight
/// out of a data reader), so there is nowhere natural to thread a dependency through. The value is
/// presentation-only — it is never persisted, and <see cref="Note.HasCustomTitle"/> stays false —
/// so treating it as ambient display state is safe.
///
/// The app layer points this at its string catalog at startup and on every language switch. Core
/// keeps working in Korean if nothing ever does.
/// </remarks>
public static class UntitledNote
{
    private const string KoreanFormat = "노트 {0}";

    private static string format = KoreanFormat;

    /// <summary>
    /// A composite format string with a single <c>{0}</c> placeholder for the note's display
    /// number. A format without that placeholder is rejected — it would render every untitled note
    /// with the same indistinguishable title.
    /// </summary>
    public static string Format
    {
        get => format;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            if (!value.Contains("{0}", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The untitled-note format must contain a '{0}' placeholder for the note number.",
                    nameof(value));
            }

            format = value;
        }
    }

    /// <summary>Restores the built-in Korean format. Exists so tests can undo a change.</summary>
    public static void ResetToDefault() => format = KoreanFormat;

    public static string TitleFor(int displayNumber) =>
        string.Format(CultureInfo.CurrentCulture, format, displayNumber);
}
