using Daynote.App.Notes;
using Daynote.Core.Agenda;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Product;

/// <summary>
/// When the <c>@</c> popup is open, and what Enter makes (docs/TODOS.md §7).
/// </summary>
/// <remarks>
/// The reference moment is the design's: Wednesday 7 October 2026, 14:30.
/// </remarks>
[TestClass]
public sealed class AgendaCaptureTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 14, 30, 0);
    private static readonly DateTimeOffset NowUtc = new(2026, 10, 7, 5, 30, 0, TimeSpan.Zero);
    private static readonly Guid Note = new("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Item = new("22222222-2222-4222-8222-222222222222");

    [TestMethod]
    public void The_title_comes_from_the_line_to_the_left()
    {
        // §7: there is nothing extra to type, so the popup shows what is already written rather
        // than asking for it.
        AgendaCaptureState state = Detect("회의자료 초안 공유 @내일");

        Assert.AreEqual("회의자료 초안 공유", state.Title);
        Assert.AreEqual("내일", state.Phrase);
        Assert.IsFalse(state.IsPrompting);
    }

    [TestMethod]
    public void An_at_with_nothing_after_it_opens_the_popup_on_its_hints()
    {
        // The design's ⑥. Not a failure — there is simply nothing to read back yet.
        AgendaCaptureState state = Detect("분기 회고 잡기 @");

        Assert.IsTrue(state.IsPrompting);
        Assert.AreEqual("분기 회고 잡기", state.Title);
    }

    [TestMethod]
    public void An_ordinary_sentence_never_opens_one()
    {
        // The design's ⑦. @ has to stay an ordinary character.
        Assert.IsNull(AgendaCapture.Detect("자료는 @지원 님께 전달", 15, Now));
        Assert.IsNull(AgendaCapture.Detect("Forward to @jiwon", 17, Now));
    }

    [TestMethod]
    public void An_email_address_is_not_a_trigger()
    {
        // The @ of an address has a letter in front of it, so it never starts a word.
        Assert.IsNull(AgendaCapture.Detect("문의 jiwon@aegisep.com", 21, Now));
    }

    [TestMethod]
    public void A_phrase_does_not_run_back_over_a_line_break()
    {
        // Pressing Enter in the body ends the thought; the popup from the line above must not
        // follow the caret down.
        Assert.IsNull(AgendaCapture.Detect("보고서 @내일\n다음 줄", 12, Now));
    }

    [TestMethod]
    public void The_chip_covers_the_at_and_exactly_what_was_read()
    {
        // §7 keeps the body as typed and the chip renders that span, so trailing words the
        // reading did not need stay outside it.
        Assert.AreEqual("@내일".Length, Detect("보고서 @내일").ChipLength);
        Assert.AreEqual("@내일".Length, Detect("보고서 @내일 에 보내기").ChipLength);
    }

    [TestMethod]
    public void A_to_do_carries_its_deadline_and_one_alert()
    {
        AgendaItem made = Compose("회의자료 초안 공유 @내일 9시", AgendaKind.Task);

        Assert.AreEqual("회의자료 초안 공유", made.Title);
        Assert.AreEqual(AgendaKind.Task, made.Kind);
        Assert.AreEqual(new WallClock(new DateTime(2026, 10, 8, 9, 0, 0)), made.DueAt);
        Assert.IsTrue(made.HasDueTime);
        // A one-off to-do carries only DUE, so the day panel finds it on the day it is owed
        // rather than the day it was typed.
        Assert.IsNull(made.StartsAt);
        CollectionAssert.AreEqual(AgendaAlert.Default.ToArray(), made.AlarmLeadMinutes.ToArray());
        Assert.AreEqual(Note, made.SourceNoteId);
        Assert.AreEqual(AgendaList.DefaultId, made.ListId);
    }

    [TestMethod]
    public void An_event_runs_an_hour_and_is_silent_unless_asked()
    {
        AgendaItem made = Compose("디자인 리뷰 준비 @금요일 3시", AgendaKind.Event);

        Assert.AreEqual(new WallClock(new DateTime(2026, 10, 9, 15, 0, 0)), made.StartsAt);
        Assert.AreEqual(new WallClock(new DateTime(2026, 10, 9, 16, 0, 0)), made.EndsAt);
        Assert.IsNull(made.DueAt);
        // A block of time is not something to be nagged about unless the user says so.
        Assert.IsEmpty(made.AlarmLeadMinutes);
    }

    [TestMethod]
    public void An_all_day_event_has_no_end()
    {
        AgendaItem made = Compose("분기 마감 @내일", AgendaKind.Event);

        Assert.AreEqual(new WallClock(new DateTime(2026, 10, 8, 0, 0, 0)), made.StartsAt);
        Assert.IsNull(made.EndsAt);
    }

    [TestMethod]
    public void A_repeating_to_do_anchors_on_its_start_the_way_a_VTODO_does()
    {
        AgendaItem made = Compose("주간 보고 작성 @매주 월 7시", AgendaKind.Task);

        Assert.AreEqual("FREQ=WEEKLY;BYDAY=MO", made.Rrule);
        Assert.AreEqual(new WallClock(new DateTime(2026, 10, 12, 7, 0, 0)), made.StartsAt);
        // A rule has a next occurrence, not a deadline.
        Assert.IsNull(made.DueAt);
        Assert.IsFalse(made.HasDueTime);
        Assert.IsTrue(made.IsSeries);
    }

    private static AgendaCaptureState Detect(string text)
    {
        AgendaCaptureState? state = AgendaCapture.Detect(text, text.Length, Now);
        Assert.IsNotNull(state, $"'{text}' opened no popup.");
        return state.Value;
    }

    private static AgendaItem Compose(string text, AgendaKind kind) =>
        AgendaCapture.Compose(Detect(text), kind, Note, Item, NowUtc);
}
