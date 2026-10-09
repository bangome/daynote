using Daynote.Core.Agenda;

namespace Daynote.Core.Tests;

/// <summary>
/// A repeating to-do with a day and no clock, ticked one occurrence at a time.
/// </summary>
/// <remarks>
/// Such an occurrence has no clock to say where it falls, and the series it is read from is
/// anchored on its first day. Both bugs here came from reading that anchor as the occurrence's day.
/// </remarks>
[TestClass]
public sealed class AgendaUndatedRepeatTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 10, 9, 5, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 9);

    [TestMethod]
    public async Task A_ticked_occurrence_stays_on_the_day_it_was_ticked()
    {
        AgendaItem series = Vitamins();
        var store = new Store(series);
        AgendaDayRow row = AgendaDay.For(Today, store.Items).Open.Single();

        AgendaItem created = await new ToggleAgendaItem(store, () => Stamp).ToggleAsync(row);

        Assert.AreEqual(Today, DateOnly.FromDateTime(created.Anchor!.Value.Value));
        AgendaDayView view = AgendaDay.For(Today, store.Items);
        Assert.AreEqual("비타민 먹기", view.Done.Single().Item.Title);
        Assert.IsEmpty(view.Open);
        // The first day keeps its own occurrence, untouched.
        Assert.HasCount(1, AgendaDay.For(new DateOnly(2026, 10, 1), store.Items).Open);
    }

    [TestMethod]
    public async Task Once_today_is_ticked_tomorrow_is_ahead_rather_than_today()
    {
        AgendaItem series = Vitamins();
        var store = new Store(series);
        await new ToggleAgendaItem(store, () => Stamp).ToggleAsync(AgendaDay.For(Today, store.Items).Open.Single());

        AgendaOutstandingView view = AgendaOutstanding.For(Today, store.Items);

        Assert.IsEmpty(view.Today);
        AgendaDayRow next = view.Later.Single();
        Assert.AreEqual(Today.AddDays(1), DateOnly.FromDateTime(next.Falls!.Value.Value));
    }

    [TestMethod]
    public void An_untouched_occurrence_falls_on_its_own_day()
    {
        AgendaDayRow row = AgendaDay.For(Today, [Vitamins()]).Open.Single();

        Assert.IsNull(row.At);
        Assert.AreEqual(Today, DateOnly.FromDateTime(row.Falls!.Value.Value));
        Assert.AreEqual(Today, DateOnly.FromDateTime(AgendaOutstanding.For(Today, [Vitamins()]).Today.Single().Falls!.Value.Value));
    }

    private static AgendaItem Vitamins() => new(
        Guid.Parse("00000000-0000-4000-8000-000000000001"),
        AgendaList.DefaultId,
        AgendaKind.Task,
        "비타민 먹기",
        string.Empty,
        "Asia/Seoul",
        StartsAt: new WallClock(new DateTime(2026, 10, 1)),
        EndsAt: null,
        DueAt: null,
        HasDueTime: false,
        Rrule: "FREQ=DAILY",
        SeriesId: null,
        RecurrenceId: null,
        AgendaStatus.NeedsAction,
        CompletedUtc: null,
        Priority: 0,
        TimelineVisibility.Auto,
        SourceNoteId: null,
        ExceptionDates: [],
        AgendaAlert.Default,
        Stamp,
        Stamp);

    /// <summary>Only what ticking touches; the rest is never called.</summary>
    private sealed class Store(params AgendaItem[] seed) : IAgendaRepository
    {
        private readonly Dictionary<Guid, AgendaItem> items = seed.ToDictionary(static i => i.Id);

        internal IReadOnlyList<AgendaItem> Items => [.. items.Values];

        public ValueTask SaveAsync(AgendaItem item, CancellationToken cancellationToken = default)
        {
            items[item.Id] = item;
            return ValueTask.CompletedTask;
        }

        public ValueTask<AgendaItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(items.GetValueOrDefault(id));

        public ValueTask<IReadOnlyList<AgendaItem>> GetAllAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Items);

        public ValueTask<IReadOnlyList<AgendaList>> GetListsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<AgendaList> CreateListAsync(Guid id, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<AgendaList?> RenameListAsync(Guid id, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<int?> DeleteListAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<AgendaItem>> GetForDateAsync(DateOnly localDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<AgendaItem>> GetSeriesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<AgendaItem>> GetForListAsync(Guid listId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
