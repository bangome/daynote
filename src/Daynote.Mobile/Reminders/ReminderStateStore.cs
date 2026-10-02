using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Daynote.Core.Domain;

namespace Daynote.Mobile.Reminders;

/// <summary>
/// What this device has scheduled, and whether it has asked for notification permission.
/// </summary>
/// <param name="PermissionAsked">The system prompt has been shown once; it is never shown again.</param>
/// <param name="ChannelName">The channel name last given to the platform.</param>
/// <param name="ChannelDescription">Its description.</param>
/// <param name="Scheduled">Every reminder handed to the platform and not cancelled since.</param>
public sealed record ReminderState(
    bool PermissionAsked,
    string ChannelName,
    string ChannelDescription,
    IReadOnlyList<Reminder> Scheduled)
{
    public static ReminderState Empty { get; } = new(false, string.Empty, string.Empty, []);
}

/// <summary>
/// <c>reminders.json</c> in the app's base data folder: the device-level record of scheduled
/// reminders, which the diff runs against and which Android's boot receiver re-arms from.
/// </summary>
/// <remarks>
/// <para>
/// Per device, not per profile: the notifications belong to the device, so the file sits beside
/// <c>profile.json</c> rather than inside a profile. A profile switch therefore diffs the new
/// profile's to-dos against what the old one left, which cancels all of the old ones even if the
/// switch was interrupted half way.
/// </para>
/// <para>
/// Neither platform can be asked "what did you schedule" in a way that serves both — AlarmManager
/// cannot list its alarms at all — so this file is the source of truth. A lost or unreadable file
/// reads as empty, and the next run simply schedules everything again (the same ids replace).
/// </para>
/// </remarks>
public sealed class ReminderStateStore
{
    public const string FileName = "reminders.json";

    private readonly string _path;

    public ReminderStateStore(string path) => _path = path ?? throw new ArgumentNullException(nameof(path));

    /// <summary>The store in <paramref name="baseRoot"/>, the app's sandbox folder.</summary>
    public static ReminderStateStore InFolder(string baseRoot) => new(Path.Combine(baseRoot, FileName));

    public ReminderState Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return ReminderState.Empty;
            }

            using FileStream stream = File.OpenRead(_path);
            StateDto? dto = JsonSerializer.Deserialize(stream, ReminderJsonContext.Default.StateDto);
            if (dto is null)
            {
                return ReminderState.Empty;
            }

            var items = new List<Reminder>();
            foreach (ItemDto item in dto.Items ?? [])
            {
                if (item.Id is { Length: > 0 } id
                    && DateTime.TryParseExact(item.At, AtFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime at)
                    && LocalDate.Parse(item.Date) is { IsSuccess: true } date
                    && Guid.TryParse(item.Note, out Guid note))
                {
                    items.Add(new Reminder(id, DateTime.SpecifyKind(at, DateTimeKind.Unspecified), item.Title ?? string.Empty, item.Body ?? string.Empty, date.Value, note));
                }
            }

            return new ReminderState(dto.Asked, dto.Channel ?? string.Empty, dto.ChannelDescription ?? string.Empty, items);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            System.Diagnostics.Trace.TraceWarning($"Reminder state unreadable, starting over: {exception.Message}");
            return ReminderState.Empty;
        }
    }

    /// <summary>Written to a temporary file and moved over, so a kill mid-write leaves the old file.</summary>
    public void Save(ReminderState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var dto = new StateDto
        {
            Asked = state.PermissionAsked,
            Channel = state.ChannelName,
            ChannelDescription = state.ChannelDescription,
            Items = [.. state.Scheduled.Select(r => new ItemDto
            {
                Id = r.Id,
                At = r.At.ToString(AtFormat, CultureInfo.InvariantCulture),
                Title = r.Title,
                Body = r.Body,
                Date = r.Date.ToString(),
                Note = r.NoteId.ToString("D"),
            })],
        };

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + ".tmp";
        using (FileStream stream = File.Create(temporary))
        {
            JsonSerializer.Serialize(stream, dto, ReminderJsonContext.Default.StateDto);
        }

        File.Move(temporary, _path, overwrite: true);
    }

    private const string AtFormat = "yyyy-MM-ddTHH:mm";

    internal sealed class StateDto
    {
        public bool Asked { get; set; }

        public string? Channel { get; set; }

        public string? ChannelDescription { get; set; }

        public List<ItemDto>? Items { get; set; }
    }

    internal sealed class ItemDto
    {
        public string? Id { get; set; }

        public string? At { get; set; }

        public string? Title { get; set; }

        public string? Body { get; set; }

        public string? Date { get; set; }

        public string? Note { get; set; }
    }
}

/// <summary>Source-generated, so the trimmed phone builds need no reflection for this file.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ReminderStateStore.StateDto))]
internal sealed partial class ReminderJsonContext : JsonSerializerContext;
