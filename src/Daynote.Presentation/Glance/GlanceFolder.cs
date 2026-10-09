using System.Text.Json;

namespace Daynote.App.Glance;

/// <summary>
/// The shared folder the app and its widgets, watch relay and menu bar meet in
/// (docs/APPLE_EXTENSIONS.md §3–4): <c>snapshot.json</c>, and an <c>actions</c> directory holding
/// one file per thing done outside the app.
/// </summary>
/// <remarks>
/// Two processes write here and neither can lock the other out, so nothing is ever written in
/// place. The snapshot goes to a temporary name and is renamed over the old one, which a reader
/// sees as either the old file or the new one and never half of each. An action is one file per
/// action, likewise renamed into place, so a widget appending while the app drains cannot lose
/// one: the app only deletes the files it has read.
/// </remarks>
public sealed class GlanceFolder(string root)
{
    public const string SnapshotFileName = "snapshot.json";

    public const string ActionsDirectoryName = "actions";

    public string Root { get; } = root ?? throw new ArgumentNullException(nameof(root));

    public string SnapshotPath => Path.Combine(Root, SnapshotFileName);

    public string ActionsPath => Path.Combine(Root, ActionsDirectoryName);

    /// <summary>Replaces the snapshot. Answers false when the content is what is there already.</summary>
    public bool WriteSnapshot(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        Directory.CreateDirectory(Root);
        if (File.Exists(SnapshotPath) && string.Equals(File.ReadAllText(SnapshotPath), json, StringComparison.Ordinal))
        {
            return false;
        }

        string temporary = Path.Combine(Root, $".{SnapshotFileName}.{Guid.NewGuid():N}");
        File.WriteAllText(temporary, json);
        File.Move(temporary, SnapshotPath, overwrite: true);
        return true;
    }

    /// <summary>Puts one action in the queue, for the app to carry out the next time it drains.</summary>
    public void Enqueue(GlanceAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Directory.CreateDirectory(ActionsPath);
        string json = JsonSerializer.Serialize(action, typeof(GlanceAction), GlanceSnapshotBuilder.Options);

        // The name sorts by when it was written, which is the order the user did things in.
        string name = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():D15}-{action.Id}.json";
        string temporary = Path.Combine(ActionsPath, "." + name);
        File.WriteAllText(temporary, json);
        File.Move(temporary, Path.Combine(ActionsPath, name), overwrite: true);
    }

    /// <summary>
    /// The queued actions, oldest first, each with the file it came from so the caller deletes
    /// exactly what it carried out. A file that cannot be read as an action is returned as null
    /// so it is deleted too rather than tried forever.
    /// </summary>
    public IReadOnlyList<(string Path, GlanceAction? Action)> ReadActions()
    {
        if (!Directory.Exists(ActionsPath))
        {
            return [];
        }

        var actions = new List<(string, GlanceAction?)>();
        foreach (string path in Directory.GetFiles(ActionsPath, "*.json").Order(StringComparer.Ordinal))
        {
            if (Path.GetFileName(path).StartsWith('.'))
            {
                continue;
            }

            GlanceAction? action;
            try
            {
                action = JsonSerializer.Deserialize(File.ReadAllText(path), typeof(GlanceAction), GlanceSnapshotBuilder.Options) as GlanceAction;
            }
            catch (JsonException)
            {
                action = null;
            }

            actions.Add((path, action));
        }

        return actions;
    }

    /// <summary>Removes an action once it has been carried out.</summary>
    public static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Gone already, or busy: the next drain tries again, and applying is idempotent.
        }
    }
}
