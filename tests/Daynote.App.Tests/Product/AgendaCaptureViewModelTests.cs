using Daynote.App.Localization;
using Daynote.App.Notes;
using Daynote.Core.Agenda;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Product;

/// <summary>
/// The <c>@</c> popup as the editor drives it: when it is open, what Tab and Esc do, and what
/// Enter makes (docs/TODOS.md §7, design §01).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class AgendaCaptureViewModelTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 14, 30, 0);
    private static readonly DateTimeOffset NowUtc = new(2026, 10, 7, 5, 30, 0, TimeSpan.Zero);
    private static readonly Guid Note = new("11111111-1111-4111-8111-111111111111");
    private AppLanguage original;

    [TestInitialize]
    public void Setup()
    {
        original = LocalizationService.Instance.Language;
        LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
    }

    [TestCleanup]
    public void Restore() => LocalizationService.Instance.SetLanguage(original);

    [TestMethod]
    public void It_opens_on_an_at_and_closes_when_what_follows_stops_being_a_date()
    {
        var capture = new AgendaCaptureViewModel();

        Update(capture, "보고서 @");
        Assert.IsTrue(capture.IsOpen);
        Assert.IsTrue(capture.IsPrompting);

        Update(capture, "보고서 @내일");
        Assert.IsTrue(capture.IsOpen);
        Assert.IsFalse(capture.IsPrompting);
        Assert.AreEqual("10월 8일 (목) 마감", capture.TaskLine);

        // @ is an ordinary character: the moment what follows reads as prose, the popup is gone.
        Update(capture, "보고서 @지원 님께");
        Assert.IsFalse(capture.IsOpen);
    }

    [TestMethod]
    public void Tab_switches_which_line_Enter_would_take()
    {
        var capture = new AgendaCaptureViewModel();
        Update(capture, "디자인 리뷰 준비 @금요일 3시");

        Assert.IsTrue(capture.IsTaskSelected);
        capture.ToggleKind();
        Assert.IsTrue(capture.IsEventSelected);

        AgendaItem? made = capture.Create(Note, NowUtc);
        Assert.IsNotNull(made);
        Assert.AreEqual(AgendaKind.Event, made.Kind);
    }

    [TestMethod]
    public void The_chosen_kind_outlives_one_capture()
    {
        // Someone booking a morning of meetings should press Tab once, not once per meeting.
        var capture = new AgendaCaptureViewModel();
        Update(capture, "A사 미팅 @내일 10시");
        capture.ToggleKind();
        _ = capture.Create(Note, NowUtc);

        Update(capture, "B사 미팅 @내일 11시");

        Assert.IsTrue(capture.IsEventSelected);
    }

    [TestMethod]
    public void Esc_makes_nothing_and_Enter_afterwards_makes_nothing_either()
    {
        var capture = new AgendaCaptureViewModel();
        Update(capture, "보고서 @내일");

        capture.Dismiss();

        Assert.IsFalse(capture.IsOpen);
        // §7: Esc creates nothing and leaves the text. The body is untouched either way, so the
        // only thing that must not happen is an item appearing anyway.
        Assert.IsNull(capture.Create(Note, NowUtc));
    }

    [TestMethod]
    public void Enter_makes_nothing_while_the_popup_is_only_prompting()
    {
        // Which is what leaves Enter as a newline the moment an @ is typed. Swallowing it there
        // would make the editor feel stuck.
        var capture = new AgendaCaptureViewModel();
        Update(capture, "보고서 @");

        Assert.IsTrue(capture.IsOpen);
        Assert.IsNull(capture.Create(Note, NowUtc));
    }

    [TestMethod]
    public void Creating_closes_the_popup()
    {
        var capture = new AgendaCaptureViewModel();
        Update(capture, "보고서 @내일");

        Assert.IsNotNull(capture.Create(Note, NowUtc));
        Assert.IsFalse(capture.IsOpen);
    }

    [TestMethod]
    public void The_note_line_appears_only_when_there_is_something_to_disagree_with()
    {
        var capture = new AgendaCaptureViewModel();

        Update(capture, "보고서 @내일");
        Assert.AreEqual(string.Empty, capture.Note);

        Update(capture, "서버 점검 공지 @9시");
        Assert.AreEqual(
            "오전 9시가 지나 내일로 읽었어요. 오늘 밤이면 ‘21시’로 입력하세요.",
            capture.Note);
    }

    [TestMethod]
    public void Every_example_it_offers_is_one_it_could_read_back()
    {
        var capture = new AgendaCaptureViewModel();
        Update(capture, "분기 회고 잡기 @");

        Assert.IsTrue(capture.IsPrompting);
        foreach (string example in capture.Examples)
        {
            Assert.IsNotNull(AgendaPhraseParser.Parse(example, Now), example);
        }
    }

    private static void Update(AgendaCaptureViewModel capture, string text) =>
        capture.Update(text, text.Length, Now);
}
