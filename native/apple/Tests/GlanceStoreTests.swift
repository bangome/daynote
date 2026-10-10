import XCTest

/// The shared folder as the extensions use it: read the app's file, queue a check, show it done.
final class GlanceStoreTests: XCTestCase {
    private var folder: URL!

    override func setUpWithError() throws {
        folder = FileManager.default.temporaryDirectory.appendingPathComponent("glance-\(UUID().uuidString)")
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: folder)
    }

    /// The app's own bytes, as `GlanceSnapshotBuilder.Serialize` writes them: camelCase, nulls left out.
    private let appJSON = """
    {"schema":1,"generatedUtc":"2026-10-07T05:30:00Z","zone":"Asia/Seoul","language":"ko","today":"2026-10-07","locked":false,
     "lists":[{"id":"00000000-0000-0000-0000-00000000da7e","name":"내 할 일","color":"#ee7f35","colorDark":"#ff9a52"}],
     "days":[{"date":"2026-10-07","todos":[
        {"id":"a","title":"회의실 예약 확인","listId":"00000000-0000-0000-0000-00000000da7e","time":"14:00","repeats":false,"done":false},
        {"id":"s","seriesId":"s","occurrence":"2026-10-07T08:00","title":"비타민 먹기","listId":"00000000-0000-0000-0000-00000000da7e","time":"08:00","repeats":true,"done":false}],
      "events":[{"id":"e","title":"디자인 리뷰","listId":"00000000-0000-0000-0000-00000000da7e","start":"16:00","end":"17:00","repeats":false}]}],
     "week":[{"date":"2026-10-04","noteCount":0,"titles":[]}],
     "favorites":[{"id":"f","date":"2026-10-07","title":"주간회의 준비","preview":"1. 2분기 지표 리뷰"}]}
    """

    func testItReadsTheFileTheAppWrites() throws {
        let store = GlanceStore(folder: folder)
        try store.save(snapshotData: Data(appJSON.utf8))

        let snapshot = try XCTUnwrap(store.load())
        let day = snapshot.day(try XCTUnwrap(LocalDay(iso: "2026-10-07")))
        XCTAssertEqual(day.todos.map(\.title), ["회의실 예약 확인", "비타민 먹기"])
        XCTAssertNil(day.todos[0].seriesId)
        XCTAssertEqual(day.events.first?.end, "17:00")
        XCTAssertEqual(snapshot.list(day.todos[0].listId)?.colorDark, "#ff9a52")
    }

    func testACheckIsQueuedForTheAppAndShownDoneAtOnce() throws {
        let store = GlanceStore(folder: folder)
        try store.save(snapshotData: Data(appJSON.utf8))
        let day = try XCTUnwrap(LocalDay(iso: "2026-10-07"))
        let vitamins = try XCTUnwrap(store.load()?.day(day).todos[1])

        try store.complete(vitamins, on: day)

        XCTAssertEqual(store.load()?.day(day).todos.map(\.done), [false, true])
        let queued = store.pendingActions()
        XCTAssertEqual(queued.count, 1)
        XCTAssertEqual(queued[0].action.type, GlanceAction.complete)
        XCTAssertEqual(queued[0].action.seriesId, "s")
        XCTAssertEqual(queued[0].action.occurrence, "2026-10-07T08:00")
        XCTAssertEqual(queued[0].action.date, "2026-10-07")

        // The app reads this with System.Text.Json: camelCase, and no nulls for it to trip on.
        let json = try XCTUnwrap(String(data: Data(contentsOf: queued[0].url), encoding: .utf8))
        XCTAssertTrue(json.contains("\"itemId\":\"s\""))
        XCTAssertFalse(json.contains("null"))
    }

    func testADeleteIsQueuedForTheAppAndTheRowLeavesAtOnce() throws {
        let store = GlanceStore(folder: folder)
        try store.save(snapshotData: Data(appJSON.utf8))
        let day = try XCTUnwrap(LocalDay(iso: "2026-10-07"))
        let vitamins = try XCTUnwrap(store.load()?.day(day).todos[1])

        try store.delete(vitamins, on: day)

        XCTAssertEqual(store.load()?.day(day).todos.map(\.title), ["회의실 예약 확인"])
        let queued = store.pendingActions()
        XCTAssertEqual(queued.count, 1)
        XCTAssertEqual(queued[0].action.type, GlanceAction.delete)
        // An occurrence: the phone deletes this one only, found by series and occurrence.
        XCTAssertEqual(queued[0].action.itemId, "s")
        XCTAssertEqual(queued[0].action.seriesId, "s")
        XCTAssertEqual(queued[0].action.occurrence, "2026-10-07T08:00")
        XCTAssertEqual(queued[0].action.date, "2026-10-07")

        let json = try XCTUnwrap(String(data: Data(contentsOf: queued[0].url), encoding: .utf8))
        XCTAssertTrue(json.contains("\"type\":\"delete\""))
        XCTAssertFalse(json.contains("null"))

        // A row already gone is left alone, and the action still goes to the app.
        XCTAssertNil(try store.delete(vitamins, on: day))
        XCTAssertEqual(store.pendingActions().count, 2)
    }

    func testAnUncompleteIsQueuedForTheAppAndShownOpenAtOnce() throws {
        let store = GlanceStore(folder: folder)
        try store.save(snapshotData: Data(appJSON.utf8))
        let day = try XCTUnwrap(LocalDay(iso: "2026-10-07"))
        let meeting = try XCTUnwrap(store.load()?.day(day).todos[0])
        try store.complete(meeting, on: day)

        try store.uncomplete(meeting, on: day)

        XCTAssertEqual(store.load()?.day(day).todos.map(\.done), [false, false])
        let queued = store.pendingActions()
        XCTAssertEqual(queued.map(\.action.type), [GlanceAction.complete, GlanceAction.uncomplete])
        XCTAssertEqual(queued[1].action.itemId, "a")
        XCTAssertNil(queued[1].action.seriesId)
        let json = try XCTUnwrap(String(data: Data(contentsOf: queued[1].url), encoding: .utf8))
        XCTAssertTrue(json.contains("\"type\":\"uncomplete\""))
        XCTAssertFalse(json.contains("seriesId"))
    }

    func testTheNewActionsDecodeAsTheAppWritesThem() throws {
        let json = """
        {"schema":1,"id":"x","type":"delete","createdUtc":"2026-10-07T05:30:00Z","itemId":"s","seriesId":"s","occurrence":"2026-10-07T08:00","date":"2026-10-07"}
        """
        let action = try JSONDecoder().decode(GlanceAction.self, from: Data(json.utf8))
        XCTAssertEqual(action.type, GlanceAction.delete)
        XCTAssertEqual(action.occurrence, "2026-10-07T08:00")
    }
}
