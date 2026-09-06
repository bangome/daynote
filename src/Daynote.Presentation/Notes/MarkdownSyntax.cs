namespace Daynote.App.Notes;

/// <summary>Result of a Markdown edit: the new text and the selection to restore.</summary>
public readonly record struct MarkdownEdit(string Text, int SelectionStart, int SelectionLength);

/// <summary>
/// Pure Markdown syntax transforms for <c>EditorToolbar</c> commands. Each operates on plain text
/// plus a selection and returns the updated text with a preserved selection; there is no rich-text
/// model. Toggling inline wrappers (bold/italic/code) removes the wrapper when already present.
/// </summary>
public static class MarkdownSyntax
{
    public static MarkdownEdit ToggleBold(string text, int start, int length) => ToggleWrap(text, start, length, "**");

    public static MarkdownEdit ToggleItalic(string text, int start, int length) => ToggleWrap(text, start, length, "*");

    public static MarkdownEdit ToggleInlineCode(string text, int start, int length) => ToggleWrap(text, start, length, "`");

    public static MarkdownEdit ToggleBulletedList(string text, int start, int length) =>
        ToggleLinePrefix(text, start, length, static _ => "- ", static line => line.StartsWith("- ", StringComparison.Ordinal) ? 2 : 0);

    public static MarkdownEdit ToggleNumberedList(string text, int start, int length) =>
        ToggleLinePrefix(text, start, length, static index => $"{index + 1}. ", NumberedPrefixLength);

    private static MarkdownEdit ToggleWrap(string text, int start, int length, string marker)
    {
        ArgumentNullException.ThrowIfNull(text);
        (start, length) = Clamp(text, start, length);
        string selected = text.Substring(start, length);
        int markerLength = marker.Length;

        bool alreadyWrapped = start >= markerLength &&
            start + length + markerLength <= text.Length &&
            text.AsSpan(start - markerLength, markerLength).SequenceEqual(marker) &&
            text.AsSpan(start + length, markerLength).SequenceEqual(marker);
        if (alreadyWrapped)
        {
            string removed = text[..(start - markerLength)] + selected + text[(start + length + markerLength)..];
            return new MarkdownEdit(removed, start - markerLength, length);
        }

        string wrapped = text[..start] + marker + selected + marker + text[(start + length)..];
        return new MarkdownEdit(wrapped, start + markerLength, length);
    }

    private static MarkdownEdit ToggleLinePrefix(
        string text,
        int start,
        int length,
        Func<int, string> prefix,
        Func<string, int> existingPrefixLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        (start, length) = Clamp(text, start, length);
        int lineStart = text.LastIndexOf('\n', Math.Max(0, start - 1)) is var nl && nl >= 0 && start > 0 ? nl + 1 : 0;
        int selectionEnd = start + length;
        int lineEnd = text.IndexOf('\n', Math.Min(selectionEnd, text.Length == 0 ? 0 : text.Length - 1));
        if (lineEnd < 0 || selectionEnd == 0)
        {
            lineEnd = text.Length;
        }

        string block = text.Substring(lineStart, lineEnd - lineStart);
        string[] lines = block.Split('\n');
        bool allPrefixed = lines.All(line => existingPrefixLength(line) > 0);
        var rebuilt = new string[lines.Length];
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            rebuilt[index] = allPrefixed
                ? line[existingPrefixLength(line)..]
                : prefix(index) + line;
        }

        string replacement = string.Join('\n', rebuilt);
        string updated = text[..lineStart] + replacement + text[lineEnd..];
        return new MarkdownEdit(updated, lineStart, replacement.Length);
    }

    private static int NumberedPrefixLength(string line)
    {
        int digits = 0;
        while (digits < line.Length && char.IsAsciiDigit(line[digits]))
        {
            digits++;
        }

        return digits > 0 && line.AsSpan(digits).StartsWith(". ") ? digits + 2 : 0;
    }

    private static (int Start, int Length) Clamp(string text, int start, int length)
    {
        start = Math.Clamp(start, 0, text.Length);
        length = Math.Clamp(length, 0, text.Length - start);
        return (start, length);
    }
}
