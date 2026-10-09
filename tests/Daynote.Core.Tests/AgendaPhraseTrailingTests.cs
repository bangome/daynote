using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Daynote.Core.Agenda;

namespace Daynote.Core.Tests;

/// <summary>
/// The watch's dictation: a date read off the end of a sentence with no <c>@</c> in it
/// (Apple Watch design §03, "받아쓰기 파서").
/// </summary>
[TestClass]
public sealed class AgendaPhraseTrailingTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 14, 30, 0);

    [TestMethod]
    public void The_date_at_the_end_is_the_phrase_and_the_rest_is_the_title()
    {
        // A7: "회의자료 초안 공유" / "오늘 오후 5:00 마감".
        (string title, AgendaPhrase phrase) = Trailing("회의자료 초안 공유 오늘 5시");
        Assert.AreEqual("회의자료 초안 공유", title);
        Assert.AreEqual(new DateTime(2026, 10, 7, 17, 0, 0), phrase.At.Value);
        Assert.IsTrue(phrase.HasTime);
    }

    [TestMethod]
    public void It_reads_the_same_phrase_the_editor_would_after_an_at()
    {
        foreach (string spoken in new[] { "비타민 먹기 매일 오전 8시", "주간회의 매주 월 10시", "Call mom on Friday" })
        {
            (string title, AgendaPhrase phrase) = Trailing(spoken);
            string typed = spoken[(title.Length + 1)..].Replace("on ", string.Empty, StringComparison.Ordinal);
            Assert.AreEqual(AgendaPhraseParser.Parse(typed, Now)!.Value.At, phrase.At, spoken);
            Assert.AreEqual(AgendaPhraseParser.Parse(typed, Now)!.Value.Rrule, phrase.Rrule, spoken);
        }
    }

    [TestMethod]
    public void What_dictation_adds_is_read_past()
    {
        Assert.AreEqual("회의자료 초안 공유", Trailing("회의자료 초안 공유 5시에").Title);
        Assert.AreEqual(new DateTime(2026, 10, 8, 0, 0, 0), Trailing("보고서 제출 내일까지.").Phrase.At.Value);
        (string title, AgendaPhrase phrase) = Trailing("Share draft slides today at 5pm.");
        Assert.AreEqual("Share draft slides", title);
        Assert.AreEqual(new DateTime(2026, 10, 7, 17, 0, 0), phrase.At.Value);
        Assert.AreEqual("Meeting", Trailing("Meeting on Friday at 3pm").Title);
    }

    [TestMethod]
    public void No_date_or_no_title_leaves_only_the_note_line()
    {
        // "날짜를 못 찾으면 위 두 줄 없이 '노트에 한 줄'만 남습니다."
        Assert.IsNull(AgendaPhraseParser.ParseTrailing("아이디어 메모", Now));
        Assert.IsNull(AgendaPhraseParser.ParseTrailing("수정 필요", Now));
        Assert.IsNull(AgendaPhraseParser.ParseTrailing("오늘 5시", Now));
        Assert.IsNull(AgendaPhraseParser.ParseTrailing("  ", Now));

        // A date at the front is not at the end: "9시 회의 준비" is a title with no date.
        Assert.IsNull(AgendaPhraseParser.ParseTrailing("9시 회의 준비", Now));

        // A bare number at the end is part of what was said, not a time: it needs a unit.
        foreach (string spoken in new[] { "아이폰 15", "Review PR 12", "Read chapter 3" })
        {
            Assert.IsNull(AgendaPhraseParser.ParseTrailing(spoken, Now), spoken);
        }

        Assert.AreEqual(new DateTime(2026, 10, 7, 17, 0, 0), Trailing("회의 5시").Phrase.At.Value);
    }

    /// <summary>
    /// The watch reads back with a Swift port of this parser and the phone creates with this one,
    /// so the two are held to one table (tests/fixtures/agenda-phrase-vectors.json) that the Swift
    /// tests read too.
    /// </summary>
    [TestMethod]
    public void The_shared_vectors_match_this_parser()
    {
        string path = VectorsPath();
        JsonNode root = JsonNode.Parse(File.ReadAllText(path))!;
        DateTime now = DateTime.ParseExact((string)root["now"]!, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
        bool write = Environment.GetEnvironmentVariable("DAYNOTE_WRITE_PHRASE_VECTORS") == "1";

        foreach (JsonNode? node in root["cases"]!.AsArray())
        {
            JsonObject entry = node!.AsObject();
            string text = (string)entry["text"]!;
            JsonNode? actual = Describe((string)entry["mode"]! == "trailing", text, now);
            if (write)
            {
                entry["expect"] = actual;
                continue;
            }

            Assert.IsTrue(
                JsonNode.DeepEquals(entry["expect"], actual),
                $"'{text}': expected {entry["expect"]?.ToJsonString() ?? "null"}, got {actual?.ToJsonString() ?? "null"}");
        }

        if (write)
        {
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }) + "\n");
        }
    }

    private static JsonNode? Describe(bool trailing, string text, DateTime now)
    {
        string? title = null;
        AgendaPhrase? phrase;
        if (trailing)
        {
            (string Title, AgendaPhrase Phrase)? read = AgendaPhraseParser.ParseTrailing(text, now);
            title = read?.Title;
            phrase = read?.Phrase;
        }
        else
        {
            phrase = AgendaPhraseParser.Parse(text, now);
        }

        if (phrase is not { } reading)
        {
            return null;
        }

        var result = new JsonObject
        {
            ["at"] = reading.At.ToString(),
            ["hasTime"] = reading.HasTime,
            ["rrule"] = reading.Rrule,
            ["rolled"] = reading.RolledToTomorrow,
        };
        if (trailing)
        {
            result["title"] = title;
        }
        else
        {
            result["length"] = reading.Length;
        }

        return result;
    }

    private static string VectorsPath()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "tests", "fixtures", "agenda-phrase-vectors.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("tests/fixtures/agenda-phrase-vectors.json was not found above the test output.");
    }

    private static (string Title, AgendaPhrase Phrase) Trailing(string spoken)
    {
        (string Title, AgendaPhrase Phrase)? read = AgendaPhraseParser.ParseTrailing(spoken, Now);
        Assert.IsNotNull(read, $"'{spoken}' was not understood.");
        return read.Value;
    }
}
