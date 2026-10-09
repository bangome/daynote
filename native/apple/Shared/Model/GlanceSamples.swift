import Foundation

/// The design files' own day — Wednesday 7 October 2026 — as a snapshot, for the widget gallery's
/// placeholder and for the renders compared against the designs.
public enum GlanceSamples {
    public static var korean: GlanceSnapshot { make(today: LocalDay(Date()), english: false) }

    public static var english: GlanceSnapshot { make(today: LocalDay(Date()), english: true) }

    private static let mine = "00000000-0000-0000-0000-00000000da7e"
    private static let work = "8f9c2b1e-5a7d-4e33-9b61-2d0c7e4f1a10"
    private static let health = "c1d2e3f4-a5b6-4c7d-8e9f-0a1b2c3d4e5f"

    public static func make(today: LocalDay, english: Bool) -> GlanceSnapshot {
        func t(_ ko: String, _ en: String) -> String { english ? en : ko }
        let weekStart = today.adding(days: -today.weekday)
        let counts = [0, 1, 0, 2, 1, 0, 1]
        let todayTitles = [t("주간회의 준비", "Weekly meeting prep"), t("배포 체크리스트", "Release checklist")]

        return GlanceSnapshot(
            generatedUtc: GlanceClock.utcStamp(Date()),
            zone: TimeZone.current.identifier,
            language: english ? "en" : "ko",
            today: today.iso,
            lists: [
                GlanceList(id: mine, name: t("내 할 일", "My tasks"), color: "#ee7f35", colorDark: "#ff9a52"),
                GlanceList(id: work, name: t("업무", "Work"), color: "#5a64c4", colorDark: "#9ca1c6"),
                GlanceList(id: health, name: t("건강", "Health"), color: "#2fa36b", colorDark: "#3dd68c"),
            ],
            days: [
                GlanceDay(
                    date: today.iso,
                    todos: [
                        GlanceTodo(id: "room", title: t("회의실 예약 확인", "Confirm room booking"), listId: mine, time: "14:00"),
                        GlanceTodo(id: "logs", title: t("퇴근 전 로그 확인", "Check logs before leaving"), listId: work, time: "18:00"),
                        GlanceTodo(id: "notes", title: t("릴리즈 노트 작성", "Write release notes"), listId: mine),
                        GlanceTodo(
                            id: "vitamins", seriesId: "vitamins", occurrence: today.iso + "T00:00",
                            title: t("비타민 먹기", "Take vitamins"), listId: health, repeats: true),
                    ],
                    events: [
                        GlanceEvent(id: "review", title: t("디자인 리뷰", "Design review"), listId: mine, start: "16:00", end: "17:00"),
                        GlanceEvent(id: "yoga", title: t("요가", "Yoga"), listId: health, start: "19:30", end: "20:30"),
                        GlanceEvent(id: "dinner", title: t("저녁 약속", "Dinner"), listId: mine, start: "21:00", end: "22:00"),
                    ]),
                GlanceDay(date: today.adding(days: 1).iso, todos: [], events: []),
            ],
            week: (0..<7).map { offset in
                let day = weekStart.adding(days: offset)
                return GlanceWeekDay(
                    date: day.iso, noteCount: day == today ? 2 : counts[offset], titles: day == today ? todayTitles : [])
            },
            favorites: [
                GlanceFavorite(
                    id: "weekly", date: today.iso, title: t("주간회의 준비", "Weekly meeting prep"),
                    preview: t("1. 2분기 지표 리뷰 · 2. 신규 기능 우선순위", "1. Q2 metrics review · 2. New feature priorities")),
                GlanceFavorite(
                    id: "monthly", date: today.adding(days: -6).iso, title: t("월간 보고 초안", "Monthly report draft"),
                    preview: t("MAU 12.4만 · 결제 전환율 3.1%", "MAU 124K · Paid conversion 3.1%")),
            ])
    }
}
