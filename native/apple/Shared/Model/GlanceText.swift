import Foundation

/// Every word the widgets and the watch show, in the app's language rather than the phone's.
///
/// The snapshot says which (`language`), because someone who runs Daynote in Korean on an
/// English phone expects its widgets in Korean too. That is also why these are not in a string
/// catalog: a catalog follows the system language, and only the widget gallery's own names and
/// descriptions — which the system shows before Daynote has said anything — use one.
public struct GlanceText: Sendable {
    public let english: Bool

    public init(english: Bool) {
        self.english = english
    }

    public init(_ snapshot: GlanceSnapshot?) {
        self.english = snapshot?.isEnglish ?? !(Locale.preferredLanguages.first ?? "ko").hasPrefix("ko")
    }

    private func t(_ ko: String, _ en: String) -> String { english ? en : ko }

    // MARK: - Headings

    public var upNext: String { t("다음 일정", "Up next") }
    public var todos: String { t("할 일", "To-dos") }
    public var todo: String { t("할 일", "To-do") }
    public var event: String { t("일정", "Event") }
    public var events: String { t("일정", "Events") }
    public var quickCapture: String { t("빠른 기록", "Quick add") }
    public var thisWeek: String { t("이번 주", "This week") }
    public var favorites: String { t("즐겨찾기", "Favorites") }
    public var today: String { t("오늘", "Today") }
    public var capture: String { t("기록", "Capture") }
    public var captureShort: String { t("기록", "Note") }
    public var title: String { t("제목", "Title") }

    public var newNote: String { t("+ 새 노트", "+ New note") }
    public var newNoteControl: String { t("새 노트", "New note") }
    public var todayNote: String { t("오늘 노트", "Today’s note") }
    public var atTodoEvent: String { t("+ 할 일·일정", "+ To-do · Event") }
    public var atTodo: String { t("+ 할 일", "+ To-do") }
    public var todoEvent: String { t("할 일·일정", "To-do · Event") }
    public var todoEventShort: String { t("할 일·일정", "To-do") }

    public func left(_ count: Int) -> String { t("남은 \(count)", "\(count) left") }
    public var leftWord: String { t("남음", "left") }
    public func todosLeft(_ count: Int) -> String { t("할 일 · 남은 \(count)", "To-dos · \(count) left") }
    public func moreToday(_ count: Int) -> String { t("오늘 일정 \(count)개 더", "\(count) more today") }
    public func notes(_ count: Int) -> String { t("노트 \(count)", "\(count) notes") }
    public func more(_ count: Int) -> String { t("+ \(count)개", "+ \(count) more") }
    public func nextTodo(at time: String?) -> String {
        guard let time else { return t("다음 할 일", "Next to-do") }
        return t("다음 할 일 · \(time)", "Next to-do · \(time)")
    }
    public func nextTodoShort(at time: String?) -> String {
        guard let time else { return t("다음 할 일", "NEXT") }
        return t("다음 할 일 · \(time)", "NEXT · \(time)")
    }
    public var todosUnit: String { t("할 일", "to-dos") }
    public func todosAfter(_ count: Int) -> String { t("이후 할 일 \(count)", "\(count) to-dos after") }

    public var nothingToday: String { t("오늘 할 일 없음", "Nothing to do today") }
    public var noFavorites: String { t("즐겨찾기한 노트 없음", "No favorite notes") }
    public var noEvents: String { t("남은 일정 없음", "No more events today") }
    public var locked: String { t("잠금을 풀면 보입니다", "Unlock Daynote to see this") }
    public var openAppOnce: String { t("Daynote를 한 번 열어 주세요", "Open Daynote once") }
    public var openOnPhone: String { t("iPhone에서 Daynote를 열어 주세요", "Open Daynote on your iPhone") }

    // MARK: - Live Activity

    public func startingSoon(_ range: String) -> String { t("곧 시작 · \(range)", "Starting soon · \(range)") }
    public func inProgress(_ range: String) -> String { t("진행 중 · \(range)", "In progress · \(range)") }
    public var now: String { t("지금", "Now") }
    public func ends(_ time: String) -> String { t("\(time) 종료", "ends \(time)") }
    public var openMeetingNote: String { t("회의 노트 열기", "Open meeting note") }
    public var openNote: String { t("회의 노트 열기", "Open note") }
    public var dismiss: String { t("알림 끄기", "Dismiss") }

    // MARK: - Watch capture

    public var noteLine: String { t("노트에 한 줄", "Add to note") }
    public var noteLineDetail: String { t("오늘 노트 끝에 그대로", "End of today’s note, as is") }
    public var done: String { t("완료", "Done") }
    public var undoDone: String { t("완료 취소", "Not done") }
    public var delete: String { t("삭제", "Delete") }
    public var justNow: String { t("방금", "Just now") }
    public var view: String { t("보기", "View") }
    public func addedTo(_ day: LocalDay) -> String {
        t("\(day.month)/\(day.day)에 추가됨", "Added to \(monthDay(day))")
    }

    // MARK: - Dates and times

    /// "10월 7일 수요일" / "Wednesday, Oct 7".
    public func longDate(_ day: LocalDay) -> String {
        english
            ? "\(Self.weekdaysLong[day.weekday]), \(monthDay(day))"
            : "\(day.month)월 \(day.day)일 \(Self.weekdaysKo[day.weekday])요일"
    }

    /// "10월 7일 수요일" / "Wed, Oct 7": the shorter form under the watch's heading.
    public func mediumDate(_ day: LocalDay) -> String {
        english
            ? "\(Self.weekdaysShort[day.weekday]), \(monthDay(day))"
            : "\(day.month)월 \(day.day)일 \(Self.weekdaysKo[day.weekday])요일"
    }

    /// "10/1" / "Oct 1".
    public func shortDate(_ day: LocalDay) -> String {
        english ? monthDay(day) : "\(day.month)/\(day.day)"
    }

    /// "일" … "토" / "S" … "S", for the week strip.
    public func weekdayLetter(_ weekday: Int) -> String {
        english ? ["S", "M", "T", "W", "T", "F", "S"][weekday] : Self.weekdaysKo[weekday]
    }

    /// "16:00–17:00", the 24-hour range the widgets print.
    public func range(_ start: String?, _ end: String?) -> String {
        guard let start else { return t("하루 종일", "All day") }
        guard let end else { return start }
        return "\(start)–\(end)"
    }

    /// "1시간 30분 후" / "in 1 h 30 min", or "지금" once it has started.
    public func until(minutes: Int) -> String {
        if minutes <= 0 { return now }
        let hours = minutes / 60, rest = minutes % 60
        switch (hours, rest) {
        case (0, _): return t("\(rest)분 후", "in \(rest) min")
        case (_, 0): return t("\(hours)시간 후", "in \(hours) h")
        default: return t("\(hours)시간 \(rest)분 후", "in \(hours) h \(rest) min")
        }
    }

    /// "오후 5:00" / "5:00 PM".
    public func clock12(_ hour: Int, _ minute: Int) -> String {
        let h = hour % 12 == 0 ? 12 : hour % 12
        let mm = String(format: "%02d", minute)
        return english ? "\(h):\(mm) \(hour < 12 ? "AM" : "PM")" : "\(hour < 12 ? "오전" : "오후") \(h):\(mm)"
    }

    private func monthDay(_ day: LocalDay) -> String { "\(Self.monthsShort[day.month - 1]) \(day.day)" }

    static let weekdaysKo = ["일", "월", "화", "수", "목", "금", "토"]
    static let weekdaysShort = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"]
    static let weekdaysLong = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"]
    static let monthsShort = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"]
}
