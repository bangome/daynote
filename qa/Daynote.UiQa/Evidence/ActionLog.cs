using System.IO;
using System.Text.Json;

namespace Daynote.UiQa.Evidence;

/// <summary>
/// An append-only, payload-redacted record of the deterministic steps a scenario performed. Entries
/// describe actions and structural observations (control names, counts, dates) only; scenario code
/// must never write note bodies, clipboard text, or image bytes into a log entry.
/// </summary>
public sealed class ActionLog
{
    private readonly List<ActionLogEntry> _entries = new();

    public IReadOnlyList<ActionLogEntry> Entries => _entries;

    public void Record(string action, string detail) =>
        _entries.Add(new ActionLogEntry(DateTimeOffset.UtcNow, action, detail));

    public void Save(string evidenceDirectory, string fileName = "action-log.json")
    {
        Directory.CreateDirectory(evidenceDirectory);
        string json = JsonSerializer.Serialize(_entries, JsonOptions);
        File.WriteAllText(Path.Combine(evidenceDirectory, fileName), json);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}

public sealed record ActionLogEntry(DateTimeOffset TimestampUtc, string Action, string Detail);
