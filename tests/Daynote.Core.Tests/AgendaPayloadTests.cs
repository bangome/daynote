using Daynote.Core.Agenda;
using Daynote.Core.Domain;
using Daynote.Core.Sync;

namespace Daynote.Core.Tests;

/// <summary>
/// The wire format for to-dos, events and their lists (docs/TODOS.md §9).
/// </summary>
/// <remarks>
/// These names are pinned: renaming one orphans every item already in the cloud. The round trip is
/// the test that matters — a field added to <see cref="AgendaItem"/> and forgotten here is a field
/// that silently never leaves the device.
/// </remarks>
[TestClass]
public sealed class AgendaPayloadTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Work = new("22222222-2222-2222-2222-222222222222");

    [TestMethod]
    public void Every_field_of_an_item_survives_the_round_trip()
    {
        AgendaItem original = new(
            Guid.NewGuid(),
            Work,
            AgendaKind.Event,
            "저녁 약속",
            "line one\nline two",
            "Asia/Seoul",
            StartsAt: new WallClock(new DateTime(2026, 10, 9, 19, 0, 0)),
            EndsAt: new WallClock(new DateTime(2026, 10, 9, 21, 0, 0)),
            DueAt: new WallClock(new DateTime(2026, 10, 9, 19, 0, 0)),
            HasDueTime: true,
            Rrule: "FREQ=WEEKLY;BYDAY=TH",
            SeriesId: null,
            RecurrenceId: null,
            AgendaStatus.Completed,
            CompletedUtc: Now,
            Priority: 5,
            TimelineVisibility.Always,
            SourceNoteId: Guid.NewGuid(),
            ExceptionDates: [new WallClock(new DateTime(2026, 10, 16, 19, 0, 0))],
            AlarmLeadMinutes: [0, 30],
            Now.AddDays(-1),
            Now);

        AgendaItem landed = Roundtrip(original);

        // The two list fields are compared on their own: a record compares them by reference, so
        // `AreEqual` on the whole item would pass for the scalars and say nothing about either.
        CollectionAssert.AreEqual(original.ExceptionDates.ToArray(), landed.ExceptionDates.ToArray());
        CollectionAssert.AreEqual(original.AlarmLeadMinutes.ToArray(), landed.AlarmLeadMinutes.ToArray());
        IReadOnlyList<WallClock> noDates = Array.Empty<WallClock>();
        IReadOnlyList<int> noAlarms = Array.Empty<int>();
        Assert.AreEqual(
            original with { ExceptionDates = noDates, AlarmLeadMinutes = noAlarms },
            landed with { ExceptionDates = noDates, AlarmLeadMinutes = noAlarms });

        // Unspecified, not Utc: a wall clock is read against the item's zone, and an instant cannot
        // be turned back into the rule that produced it (§6).
        Assert.AreEqual(DateTimeKind.Unspecified, landed.StartsAt!.Value.Value.Kind);
    }

    [TestMethod]
    public void An_override_keeps_both_halves_of_its_key()
    {
        AgendaItem series = Item() with { Rrule = "FREQ=DAILY" };
        AgendaItem original = Item() with
        {
            SeriesId = series.Id,
            RecurrenceId = new WallClock(new DateTime(2026, 10, 9, 7, 0, 0)),
        };

        AgendaItem landed = Roundtrip(original);

        Assert.AreEqual(series.Id, landed.SeriesId);
        Assert.AreEqual(original.RecurrenceId, landed.RecurrenceId);
        Assert.IsTrue(landed.IsOverride);
        Assert.IsFalse(landed.IsSeries);
    }

    [TestMethod]
    public void The_property_names_are_iCalendars_where_iCalendar_has_one()
    {
        // This shape is also what the .ics feed and CalDAV get built from, so two vocabularies for
        // one field is how the mapping starts drifting (§3).
        string json = AgendaPayloadCodec.Serialize(Item() with
        {
            Rrule = "FREQ=DAILY",
            ExceptionDates = [new WallClock(new DateTime(2026, 10, 16, 19, 0, 0))],
        });

        foreach (string name in new[] { "rrule", "exdate", "status", "priority", "due_at", "tz" })
        {
            StringAssert.Contains(json, $"\"{name}\":");
        }
    }

    [TestMethod]
    public void A_payload_that_is_not_valid_JSON_is_reported_rather_than_thrown()
    {
        // The bytes decrypted, so anything wrong past this point is a version or corruption problem
        // to report and skip — not a crash inside a background sync.
        DomainResult<AgendaItem> result =
            AgendaPayloadCodec.DeserializeItem(Guid.NewGuid().ToString(), "{ not json", Now);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(DomainErrorCode.MalformedSyncPayload, result.Error.Code);
    }

    [TestMethod]
    public void An_unknown_enumeration_value_is_reported_rather_than_guessed()
    {
        string json = AgendaPayloadCodec.Serialize(Item()).Replace(
            "\"status\":\"needs_action\"",
            "\"status\":\"in_process\"",
            StringComparison.Ordinal);

        DomainResult<AgendaItem> result =
            AgendaPayloadCodec.DeserializeItem(Guid.NewGuid().ToString(), json, Now);

        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void A_list_never_arrives_claiming_to_be_the_default()
    {
        // `is_default` is unique in the schema, so honouring a remote claim would make applying a
        // page depend on the order its rows arrive in. The default is the fixed id and nothing else.
        string json = AgendaPayloadCodec.Serialize(new AgendaList(Work, "Work", 1, true, Now, Now));

        DomainResult<AgendaList> landed =
            AgendaPayloadCodec.DeserializeList(Work.ToString(), json, Now);

        Assert.IsTrue(landed.IsSuccess);
        Assert.IsFalse(landed.Value.IsDefault);

        DomainResult<AgendaList> actualDefault = AgendaPayloadCodec.DeserializeList(
            AgendaList.DefaultId.ToString(), json, Now);
        Assert.IsTrue(actualDefault.Value.IsDefault);
    }

    [TestMethod]
    public void A_dangling_source_note_is_kept_and_an_unreadable_one_is_dropped()
    {
        // It only drives a jump back to where the item was captured, so losing it is never a reason
        // to drop the item itself.
        Guid note = Guid.NewGuid();
        Assert.AreEqual(note, Roundtrip(Item() with { SourceNoteId = note }).SourceNoteId);

        string json = AgendaPayloadCodec.Serialize(Item() with { SourceNoteId = note }).Replace(
            note.ToString(),
            "not-a-guid",
            StringComparison.Ordinal);
        DomainResult<AgendaItem> result =
            AgendaPayloadCodec.DeserializeItem(Guid.NewGuid().ToString(), json, Now);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNull(result.Value.SourceNoteId);
    }

    private static AgendaItem Roundtrip(AgendaItem item)
    {
        DomainResult<AgendaItem> result = AgendaPayloadCodec.DeserializeItem(
            item.Id.ToString(),
            AgendaPayloadCodec.Serialize(item),
            item.UpdatedUtc);

        Assert.IsTrue(result.IsSuccess, result.IsSuccess ? null : result.Error.Message);
        return result.Value;
    }

    private static AgendaItem Item() => new(
        Guid.NewGuid(),
        Work,
        AgendaKind.Task,
        "Buy milk",
        string.Empty,
        "Asia/Seoul",
        StartsAt: null,
        EndsAt: null,
        DueAt: null,
        HasDueTime: false,
        Rrule: null,
        SeriesId: null,
        RecurrenceId: null,
        AgendaStatus.NeedsAction,
        CompletedUtc: null,
        Priority: 0,
        TimelineVisibility.Auto,
        SourceNoteId: null,
        ExceptionDates: [],
        AlarmLeadMinutes: [],
        Now,
        Now);
}
