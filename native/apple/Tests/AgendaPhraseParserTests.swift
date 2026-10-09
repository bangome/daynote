import XCTest

/// The watch's parser against the table the C# parser wrote (tests/fixtures/agenda-phrase-vectors.json).
final class AgendaPhraseParserTests: XCTestCase {
    private struct Vectors: Decodable {
        struct Case: Decodable {
            struct Expect: Decodable, Equatable {
                var at: String
                var hasTime: Bool
                var rrule: String?
                var rolled: Bool
                var length: Int?
                var title: String?
            }

            var mode: String
            var text: String
            var expect: Expect?
        }

        var now: String
        var cases: [Case]
    }

    func testEveryVectorReadsAsTheAppReadsIt() throws {
        let url = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("tests/fixtures/agenda-phrase-vectors.json")
        let vectors = try JSONDecoder().decode(Vectors.self, from: Data(contentsOf: url))
        let now = try XCTUnwrap(LocalMoment(iso: vectors.now))
        XCTAssertGreaterThan(vectors.cases.count, 40)

        for vector in vectors.cases {
            let actual: Vectors.Case.Expect?
            if vector.mode == "trailing" {
                actual = AgendaPhraseParser.parseTrailing(vector.text, now: now).map {
                    .init(at: $0.phrase.at.iso, hasTime: $0.phrase.hasTime, rrule: $0.phrase.rrule,
                          rolled: $0.phrase.rolledToTomorrow, length: nil, title: $0.title)
                }
            } else {
                actual = AgendaPhraseParser.parse(vector.text, now: now).map {
                    .init(at: $0.at.iso, hasTime: $0.hasTime, rrule: $0.rrule, rolled: $0.rolledToTomorrow,
                          length: $0.length, title: nil)
                }
            }
            XCTAssertEqual(actual, vector.expect, "'\(vector.text)'")
        }
    }

    func testTheDesignsReadbacksInBothLanguages() throws {
        let now = try XCTUnwrap(LocalMoment(iso: "2026-10-07T14:30"))

        // A7.
        let korean = CaptureReadback.read("회의자료 초안 공유 오늘 5시", now: now, text: GlanceText(english: false))
        XCTAssertEqual(korean.title, "회의자료 초안 공유")
        XCTAssertEqual(korean.task, "오늘 오후 5:00 마감")
        XCTAssertEqual(korean.event, "오늘 오후 5–6시")

        // A11.
        let english = CaptureReadback.read("Share draft slides today at 5pm", now: now, text: GlanceText(english: true))
        XCTAssertEqual(english.title, "Share draft slides")
        XCTAssertEqual(english.task, "Due today, 5:00 PM")
        XCTAssertEqual(english.event, "Today 5–6 PM")

        // A8: a bare 5 o'clock in the morning that had passed reads as tomorrow, and says the date.
        let rolled = CaptureReadback.read("회의자료 초안 공유 오전 5시", now: now, text: GlanceText(english: false))
        XCTAssertEqual(rolled.task, "내일 10/8 오전 5:00")
        XCTAssertEqual(rolled.event, "내일 오전 5–6시")
        XCTAssertTrue(rolled.rolledToTomorrow)

        // No date: only the note line is offered.
        let plain = CaptureReadback.read("아이디어 메모", now: now, text: GlanceText(english: false))
        XCTAssertFalse(plain.hasDate)
        XCTAssertEqual(plain.title, "아이디어 메모")
    }

    func testRepeatsAndAllDayRead() throws {
        let now = try XCTUnwrap(LocalMoment(iso: "2026-10-07T14:30"))
        let ko = GlanceText(english: false)
        XCTAssertEqual(CaptureReadback.read("비타민 먹기 매일 오전 8시", now: now, text: ko).task, "매일 오전 8:00")
        XCTAssertEqual(CaptureReadback.read("주간회의 매주 월 10시", now: now, text: ko).event, "매주 월요일 오전 10–11시")
        XCTAssertEqual(CaptureReadback.read("보고서 제출 내일까지", now: now, text: ko).event, "내일 · 하루 종일")
        XCTAssertEqual(CaptureReadback.read("Pay rent by 10/25", now: now, text: GlanceText(english: true)).task, "Due Sun, Oct 25")
    }
}
