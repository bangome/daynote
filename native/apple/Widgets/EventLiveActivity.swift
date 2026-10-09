import ActivityKit
import AppIntents
import SwiftUI
import WidgetKit

/// The Live Activity for an event (design §08): from fifteen minutes before it until it ends,
/// on the Lock Screen and in the Dynamic Island. Never for a to-do.
///
/// Local only — no push. The countdown and the progress bar are the system's own timer views, so
/// they move without updates; the label changes from "곧 시작" to "진행 중" when the content goes
/// stale at the start time, which is the one moment the system redraws it unasked.
struct EventLiveActivity: Widget {
    var body: some WidgetConfiguration {
        ActivityConfiguration(for: EventActivityAttributes.self) { context in
            EventBanner(attributes: context.attributes, state: context.state, isStale: context.isStale, now: Date())
                .activityBackgroundTint(nil)
        } dynamicIsland: { context in
            let text = GlanceText(english: context.attributes.english)
            return DynamicIsland {
                DynamicIslandExpandedRegion(.leading) {
                    DaynoteMark(size: 22).padding(.leading, 4)
                }
                DynamicIslandExpandedRegion(.trailing) {
                    Countdown(state: context.state, stale: context.isStale, now: Date(), size: 26)
                }
                DynamicIslandExpandedRegion(.center) {
                    IslandTitle(attributes: context.attributes, state: context.state)
                }
                DynamicIslandExpandedRegion(.bottom) {
                    ActivityButtons(attributes: context.attributes, text: text, compact: true)
                }
            } compactLeading: {
                DaynoteMark(size: 18)
            } compactTrailing: {
                Countdown(state: context.state, stale: context.isStale, now: Date(), size: 14)
            } minimal: {
                DaynoteMark(size: 18)
            }
            .widgetURL(URL(string: context.attributes.noteLink))
            .keylineTint(Color(hex: "#ff9a52"))
        }
    }

    static func clock(_ date: Date) -> String {
        let parts = Calendar.current.dateComponents([.hour, .minute], from: date)
        return String(format: "%02d:%02d", parts.hour ?? 0, parts.minute ?? 0)
    }
}

/// "16:00–17:00 · 업무" over the title, in the expanded island.
struct IslandTitle: View {
    var attributes: EventActivityAttributes
    var state: EventActivityAttributes.ContentState

    var body: some View {
        let range = "\(EventLiveActivity.clock(state.start))–\(EventLiveActivity.clock(state.end))"
        VStack(alignment: .leading, spacing: 1) {
            Text(attributes.listName.isEmpty ? range : "\(range) · \(attributes.listName)")
                .font(.system(size: 12)).foregroundStyle(.secondary)
            Text(state.title).font(.system(size: 17, weight: .heavy)).lineLimit(1)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

/// "9:42" to the start; the end time once it has begun.
struct Countdown: View {
    var state: EventActivityAttributes.ContentState
    var stale: Bool
    var now: Date
    var size: CGFloat

    var body: some View {
        if stale || now >= state.start {
            Text(EventLiveActivity.clock(state.end)).font(.system(size: size, weight: .heavy).monospacedDigit())
                .foregroundStyle(Color(hex: "#ff9a52"))
        } else {
            TimerText(from: now, to: state.start)
                .font(.system(size: size, weight: .heavy).monospacedDigit())
                .foregroundStyle(Color(hex: "#ff9a52"))
                .multilineTextAlignment(.trailing)
                .frame(maxWidth: size * 3)
        }
    }
}

/// The Lock Screen banner (W18, W19).
struct EventBanner: View {
    var attributes: EventActivityAttributes
    var state: EventActivityAttributes.ContentState
    var isStale: Bool
    var now: Date
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        let text = GlanceText(english: attributes.english)
        let theme = GlanceTheme.of(scheme)
        let started = isStale || now >= state.start
        let range = "\(EventLiveActivity.clock(state.start))–\(EventLiveActivity.clock(state.end))"

        VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .top, spacing: 10) {
                DaynoteMark(size: 22)
                VStack(alignment: .leading, spacing: 1) {
                    Text(started ? text.inProgress(range) : text.startingSoon(range))
                        .font(.system(size: 11.5)).foregroundStyle(theme.secondary)
                    Text(state.title).font(.system(size: 16, weight: .heavy)).foregroundStyle(theme.text).lineLimit(1)
                }
                Spacer(minLength: 4)
                VStack(alignment: .trailing, spacing: 0) {
                    if started {
                        Text(text.now).font(.system(size: 24, weight: .heavy)).foregroundStyle(theme.accent)
                        Text(text.ends(EventLiveActivity.clock(state.end))).font(.system(size: 10.5)).foregroundStyle(theme.muted)
                    } else {
                        TimerText(from: now, to: state.start)
                            .font(.system(size: 24, weight: .heavy).monospacedDigit()).foregroundStyle(theme.accent)
                            .multilineTextAlignment(.trailing).frame(width: 80, alignment: .trailing)
                        Text(text.leftWord).font(.system(size: 10.5)).foregroundStyle(theme.muted)
                    }
                }
            }
            ActivityProgress(
                range: started ? state.start...state.end
                    : state.start.addingTimeInterval(TimeInterval(-EventActivity.leadMinutes * 60))...state.start,
                now: now)
            ActivityButtons(attributes: attributes, text: text, compact: false)
        }
        .padding(16)
        .background(theme.background)
    }
}

struct ActivityButtons: View {
    var attributes: EventActivityAttributes
    var text: GlanceText
    var compact: Bool
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        let theme = compact ? GlanceTheme.watch : GlanceTheme.of(scheme)
        HStack(spacing: 8) {
            Link(destination: URL(string: attributes.noteLink)!) {
                label(compact ? text.openNote : text.openMeetingNote, theme: theme)
            }
            Button(intent: DismissEventActivityIntent(eventId: attributes.eventId)) {
                label(text.dismiss, theme: theme)
            }
            .buttonStyle(.plain)
        }
    }

    private func label(_ title: String, theme: GlanceTheme) -> some View {
        Text(title).font(.system(size: 13, weight: .bold)).foregroundStyle(theme.text)
            .frame(maxWidth: .infinity, minHeight: 34)
            .background(Capsule().fill(compact ? Color.white.opacity(0.14) : theme.chip))
    }
}

/// The system's own countdown, which keeps moving with the app asleep. A render pins it, since a
/// live timer would print whatever the build machine's clock said.
struct TimerText: View {
    var from: Date
    var to: Date

    var body: some View {
        #if DAYNOTE_RENDER_TESTS
        let seconds = max(Int(to.timeIntervalSince(from)), 0)
        Text(String(format: "%d:%02d", seconds / 60, seconds % 60))
        #else
        Text(timerInterval: from...to, countsDown: true)
        #endif
    }
}

/// The bar under the banner: the lead-in before the start, the event itself after it.
struct ActivityProgress: View {
    var range: ClosedRange<Date>
    var now: Date

    var body: some View {
        Group {
            #if DAYNOTE_RENDER_TESTS
            let fraction = min(max(now.timeIntervalSince(range.lowerBound) / range.upperBound.timeIntervalSince(range.lowerBound), 0), 1)
            GeometryReader { space in
                ZStack(alignment: .leading) {
                    Capsule().fill(Color.gray.opacity(0.25))
                    Capsule().fill(Color(hex: "#ff9a52")).frame(width: space.size.width * fraction)
                }
            }
            .frame(height: 4)
            #else
            ProgressView(timerInterval: range, countsDown: false, label: { EmptyView() }, currentValueLabel: { EmptyView() })
            #endif
        }
        .tint(Color(hex: "#ff9a52"))
    }
}
