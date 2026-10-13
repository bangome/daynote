using Daynote.Core.Agenda;

namespace Daynote.Desktop.Tests;

/// <summary>
/// The to-dos and events of the v2 desktop design's <c>sampleData()</c> (Daynote Desktop B v2), as
/// entities: what the day panel, the 할 일 view and the timeline are drawn over.
/// </summary>
internal static class AgendaSeed
{
    internal const string Work = "업무";
    internal const string Life = "개인";
    internal const string Shopping = "장보기";

    /// <summary>One item, shaped the way <c>AgendaCapture.Compose</c> shapes one.</summary>
    internal static AgendaItem Item(
        string title,
        AgendaKind kind,
        DateOnly day,
        TimeOnly? time = null,
        TimeOnly? end = null,
        string? rrule = null,
        Guid? list = null,
        bool done = false,
        TimelineVisibility visibility = TimelineVisibility.Auto)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var at = new WallClock(day.ToDateTime(time ?? TimeOnly.MinValue));
        bool repeats = rrule is not null;
        return new AgendaItem(
            Guid.NewGuid(),
            list ?? AgendaList.DefaultId,
            kind,
            title,
            string.Empty,
            AgendaZone.Local(),
            StartsAt: kind == AgendaKind.Event || repeats ? at : null,
            EndsAt: kind == AgendaKind.Event && end is { } stop ? new WallClock(day.ToDateTime(stop)) : null,
            DueAt: kind == AgendaKind.Task ? at : null,
            HasDueTime: kind == AgendaKind.Task && time is not null,
            rrule,
            SeriesId: null,
            RecurrenceId: null,
            done ? AgendaStatus.Completed : AgendaStatus.NeedsAction,
            CompletedUtc: done ? now : null,
            Priority: 0,
            visibility,
            SourceNoteId: null,
            ExceptionDates: [],
            AlarmLeadMinutes: kind == AgendaKind.Task ? AgendaAlert.Default : AgendaAlert.None,
            now,
            now);
    }

    /// <summary>
    /// The design's to-dos and events around <paramref name="today"/>, filed in the three lists
    /// whose ids are given.
    /// </summary>
    internal static IReadOnlyList<AgendaItem> Sample(DateOnly today, Guid work, Guid life, Guid shopping) =>
    [
        Item("회의자료 초안 공유", AgendaKind.Task, today.AddDays(1), new TimeOnly(10, 0), list: work),
        Item("회의실 예약 확인", AgendaKind.Task, today, new TimeOnly(17, 0), list: work),
        Item("지난주 액션아이템 정리", AgendaKind.Task, today, list: work, done: true),
        Item("스테이징 회귀 테스트", AgendaKind.Task, today.AddDays(2), new TimeOnly(14, 0), list: work),
        Item("릴리즈 노트 작성", AgendaKind.Task, today, list: work),
        Item("DB 마이그레이션 스크립트 리뷰", AgendaKind.Task, today, list: work, done: true),
        Item("속도 개선 이슈 티켓 생성", AgendaKind.Task, today.AddDays(1), new TimeOnly(12, 0), list: work),
        Item("비타민 먹기", AgendaKind.Task, today.AddDays(-30), new TimeOnly(8, 0), rrule: "FREQ=DAILY", list: life),
        Item("우유 사기", AgendaKind.Task, today, list: shopping),
        Item("주간 회고 작성", AgendaKind.Task, today.AddDays(-28), rrule: "FREQ=WEEKLY", visibility: TimelineVisibility.Always),
        Item("스탠드업", AgendaKind.Event, today.AddDays(-30), new TimeOnly(9, 0), new TimeOnly(9, 30),
            rrule: "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR", list: work),
        Item("A사 정기 미팅", AgendaKind.Event, today.AddDays(-1), new TimeOnly(15, 0), new TimeOnly(16, 0), list: work),
        Item("점심 약속", AgendaKind.Event, today, new TimeOnly(12, 30), new TimeOnly(13, 30), list: life),
        Item("디자인 리뷰", AgendaKind.Event, today, new TimeOnly(16, 0), new TimeOnly(17, 0), list: work),
        Item("분기 마감", AgendaKind.Event, today, list: work),
        Item("프론트엔드 2차 면접", AgendaKind.Event, today.AddDays(1), new TimeOnly(14, 0), new TimeOnly(15, 0), list: work),
    ];
}
