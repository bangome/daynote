using Daynote.Core.Agenda;

namespace Daynote.Core.Tests;

/// <summary>
/// The natural language after <c>@</c> (docs/TODOS.md §7).
/// </summary>
/// <remarks>
/// Every reading here is one the design mock shows a readback line for
/// (docs/design-renewal/Daynote B Tasks - Events.dc.html §01). Its reference moment is the one
/// used throughout: Wednesday 7 October 2026, 14:30. That matters — half the examples turn on
/// what has already passed today.
/// </remarks>
[TestClass]
public sealed class AgendaPhraseTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 14, 30, 0);

    [TestMethod]
    public void A_day_on_its_own_has_no_time()
    {
        // "할 일 10월 8일 (목) 마감" / "일정 10월 8일 (목) · 하루 종일"
        foreach (string typed in new[] { "내일", "tomorrow" })
        {
            AgendaPhrase phrase = Parse(typed);
            Assert.AreEqual(new DateTime(2026, 10, 8, 0, 0, 0), phrase.At.Value, typed);
            Assert.IsFalse(phrase.HasTime, typed);
            Assert.IsNull(AgendaPhraseParser.EventEnd(phrase), typed);
        }

        Assert.AreEqual(new DateTime(2026, 10, 7, 0, 0, 0), Parse("오늘").At.Value);
        Assert.AreEqual(new DateTime(2026, 10, 9, 0, 0, 0), Parse("모레").At.Value);
    }

    [TestMethod]
    public void A_weekday_means_the_next_one_and_an_event_runs_an_hour()
    {
        // "할 일 10월 9일 (금) 오후 3:00 마감" / "일정 10월 9일 (금) 오후 3:00–4:00"
        foreach (string typed in new[] { "금요일 3시", "fri 3pm" })
        {
            AgendaPhrase phrase = Parse(typed);
            Assert.AreEqual(new DateTime(2026, 10, 9, 15, 0, 0), phrase.At.Value, typed);
            Assert.IsTrue(phrase.HasTime, typed);
            Assert.AreEqual(new DateTime(2026, 10, 9, 16, 0, 0), AgendaPhraseParser.EventEnd(phrase)!.Value.Value, typed);
        }
    }

    [TestMethod]
    public void Today_is_not_the_weekday_you_are_standing_on()
    {
        // Wednesday, typed on a Wednesday: a day that has already started is not what someone is
        // scheduling.
        Assert.AreEqual(new DateTime(2026, 10, 14, 0, 0, 0), Parse("수요일").At.Value);
    }

    [TestMethod]
    public void A_repeat_reads_back_its_next_occurrence()
    {
        // "매주 월요일 오전 7:00 · 다음 10/12"
        foreach (string typed in new[] { "매주 월 7시", "every mon 7am" })
        {
            AgendaPhrase phrase = Parse(typed);
            Assert.AreEqual("FREQ=WEEKLY;BYDAY=MO", phrase.Rrule, typed);
            Assert.AreEqual(new DateTime(2026, 10, 12, 7, 0, 0), phrase.At.Value, typed);
        }

        Assert.AreEqual("FREQ=DAILY", Parse("매일 7시").Rrule);
        Assert.AreEqual("FREQ=DAILY", Parse("every day 7am").Rrule);
    }

    [TestMethod]
    public void A_time_that_has_passed_today_is_tomorrow_and_says_so()
    {
        // The one reading a user is most likely to disagree with, so the popup prints
        // "오전 9시가 지나 내일로 읽었어요. 오늘 밤이면 '21시'로 입력하세요."
        foreach (string typed in new[] { "9시", "9" })
        {
            AgendaPhrase phrase = Parse(typed);
            Assert.AreEqual(new DateTime(2026, 10, 8, 9, 0, 0), phrase.At.Value, typed);
            Assert.IsTrue(phrase.RolledToTomorrow, typed);
        }

        // Still ahead at 14:30, so it stays today and there is nothing to warn about.
        AgendaPhrase tonight = Parse("21시");
        Assert.AreEqual(new DateTime(2026, 10, 7, 21, 0, 0), tonight.At.Value);
        Assert.IsFalse(tonight.RolledToTomorrow);
    }

    [TestMethod]
    public void A_small_bare_hour_is_the_afternoon()
    {
        // "3시" is three in the afternoon in every note anyone writes. Seven and later are left
        // alone, which is where the guess stops being obvious — and is why the readback exists.
        Assert.AreEqual(15, Parse("3시").At.Value.Hour);
        Assert.AreEqual(15, Parse("3:30").At.Value.Hour);
        Assert.AreEqual(30, Parse("3:30").At.Value.Minute);
        Assert.AreEqual(7, Parse("내일 7시").At.Value.Hour);
        // Said explicitly, it is believed either way.
        Assert.AreEqual(3, Parse("오전 3시").At.Value.Hour);
        Assert.AreEqual(3, Parse("3am").At.Value.Hour);
        Assert.AreEqual(14, Parse("14:30").At.Value.Hour);
        Assert.AreEqual(0, Parse("내일 12am").At.Value.Hour);
    }

    [TestMethod]
    public void An_explicit_date_takes_a_time_after_it()
    {
        // "할 일 10월 8일 (목) 오전 9:00 마감"
        foreach (string typed in new[] { "10/8 9시", "10/8 9am", "10월 8일 9시" })
        {
            Assert.AreEqual(new DateTime(2026, 10, 8, 9, 0, 0), Parse(typed).At.Value, typed);
        }
    }

    [TestMethod]
    public void A_date_already_past_this_year_is_next_year()
    {
        // Nobody types a year. "@1/3" in October is January, not ten months ago.
        Assert.AreEqual(new DateTime(2027, 1, 3, 0, 0, 0), Parse("1/3").At.Value);
    }

    [TestMethod]
    public void Nothing_typed_yet_is_not_a_reading()
    {
        // The popup is open — it opened on the "@" — and shows its hints: 오늘 / 내일 / 금요일 3시 /
        // 매주 월. There is simply nothing to read back.
        Assert.IsNull(AgendaPhraseParser.Parse(string.Empty, Now));
        Assert.IsNull(AgendaPhraseParser.Parse("   ", Now));
    }

    [TestMethod]
    public void An_ordinary_sentence_gets_no_popup_at_all()
    {
        // "자료는 @지원 님께 전달" and "Forward the deck to @jiwon". @ is an ordinary character and
        // has to stay one.
        foreach (string typed in new[] { "지원 님께 전달", "jiwon", "jiwon@aegisep.com", "everyone" })
        {
            Assert.IsNull(AgendaPhraseParser.Parse(typed, Now), typed);
        }
    }

    [TestMethod]
    public void A_one_letter_korean_weekday_only_counts_after_a_word_meaning_every()
    {
        // On its own it is far too eager: these are ordinary words, not Sunday and Wednesday.
        Assert.IsNull(AgendaPhraseParser.Parse("일정 잡기", Now));
        Assert.IsNull(AgendaPhraseParser.Parse("수정 필요", Now));

        Assert.AreEqual("FREQ=WEEKLY;BYDAY=SU", Parse("매주 일").Rrule);
    }

    [TestMethod]
    public void A_number_that_cannot_be_an_hour_is_not_one()
    {
        Assert.IsNull(AgendaPhraseParser.Parse("2026", Now));
        Assert.IsNull(AgendaPhraseParser.Parse("99", Now));
    }

    [TestMethod]
    public void The_length_consumed_is_what_the_editor_turns_into_a_chip()
    {
        // §7 keeps the body exactly as typed; the chip renders that span rather than replacing it,
        // so the span has to be measured rather than guessed.
        Assert.AreEqual("내일".Length, Parse("내일").Length);
        Assert.AreEqual("매주 월 7시".Length, Parse("매주 월 7시").Length);
        // Trailing text the reading did not need is left out of the chip.
        Assert.AreEqual("내일".Length, Parse("내일 에 보내기").Length);
    }

    private static AgendaPhrase Parse(string typed)
    {
        AgendaPhrase? parsed = AgendaPhraseParser.Parse(typed, Now);
        Assert.IsNotNull(parsed, $"'{typed}' was not understood.");
        return parsed.Value;
    }
}
