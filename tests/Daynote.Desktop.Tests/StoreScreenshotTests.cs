using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.App.Shell.Product;
using Daynote.Core.Domain;
using Daynote.Core.Notes;
using Daynote.Desktop.ViewModels;
using Daynote.Desktop.Views;

namespace Daynote.Desktop.Tests;

/// <summary>
/// Renders the Store listing's screenshots from the shipping shell.
/// </summary>
/// <remarks>
/// A test rather than a script, and that is the point. The four images under
/// <c>docs/brand/microsoft-store/</c> were captured by hand on 2026-07-27 and quietly rotted: they
/// predate the v3 palette and the account window, and one of them showed the clipboard drawer — a
/// feature deleted in August. A listing that advertises a removed feature is a certification
/// problem and a refund problem, and nothing in the repository noticed for six weeks.
/// <para>
/// Rendering them from the real service graph means they cannot drift again without this test
/// rewriting them. Every piece of content below is produced by the app rather than drawn over it:
/// the to-do rows come from parsing note bodies, the chips from the tag command, the file cards
/// from the real content-addressed store. A screenshot assembled any other way could show a layout
/// the app cannot actually produce.
/// </para>
/// </remarks>
[TestClass]
public sealed class StoreScreenshotTests
{
    /// <summary>The Store's floor is 1366x768; this clears it and matches a common laptop.</summary>
    private const int Width = 1440;
    private const int Height = 900;

    private static readonly string Root = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "brand", "microsoft-store"));

    [TestMethod]
    public void Korean_listing_images() => Capture(AppLanguage.Korean, Root, "daynote-store-ko");

    [TestMethod]
    public void English_listing_images() =>
        Capture(AppLanguage.English, Path.Combine(Root, "en"), "daynote-store-en");

    private static void Capture(AppLanguage language, string directory, string prefix)
    {
        AppLanguage original = LocalizationService.Instance.Language;
        try
        {
            TestServices.WithInitialisedShell(Width, Height, (window, shell) =>
            {
                LocalizationService.Instance.SetLanguage(language);
                Pump(window);

                Seed(shell, language);
                Directory.CreateDirectory(directory);

                shell.ActiveTab = RightTab.Todo;
                Shoot(window, directory, $"{prefix}-01-overview");

                shell.ActiveTab = RightTab.Tags;
                Shoot(window, directory, $"{prefix}-02-tags");

                shell.ActiveTab = RightTab.Files;
                Shoot(window, directory, $"{prefix}-03-files");

                shell.ActiveTab = RightTab.Todo;
                shell.IsDark = true;
                Shoot(window, directory, $"{prefix}-04-dark");
                shell.IsDark = false;
            });
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(original);
        }
    }

    private static void Seed(DesktopShellViewModel shell, AppLanguage language)
    {
        bool korean = language == AppLanguage.Korean;
        LocalDate today = LocalDates.FromDateOnly(DateOnly.FromDateTime(DateTime.Now));

        // Earlier days first, so the calendar shows a month with a history in it. A calendar with
        // one marked square is the least representative thing a date-organised app can show.
        foreach (Entry entry in korean ? KoreanHistory : EnglishHistory)
        {
            Wait(shell.SelectDateAsync(LocalDates.AddDays(today, -entry.DaysAgo)));
            Pump();
            WriteNote(shell, entry.Title, entry.Body);
        }

        Wait(shell.SelectDateAsync(today));
        Pump();

        foreach (Entry entry in korean ? KoreanToday : EnglishToday)
        {
            WriteNote(shell, entry.Title, entry.Body);
        }

        // Back to the first note of the day: the one the shots are composed around.
        shell.Notes.SelectedTab = shell.Notes.Tabs[0];
        Pump();

        foreach (string tag in korean ? KoreanTags : EnglishTags)
        {
            Wait(shell.Notes.AddTagAsync(shell.Notes.SelectedTab, tag));
        }

        foreach ((string name, int bytes) in korean ? KoreanAttachments : EnglishAttachments)
        {
            using var content = new MemoryStream(Filler(name, bytes));
            Wait(shell.Files.AddFromStreamAsync(name, content));
        }

        Wait(shell.Todo.RefreshAsync());
        Wait(shell.TagPanel.RefreshAsync());
        Pump();
    }

    /// <summary>One note, titled and saved, the way the add button and the rename do it.</summary>
    private static void WriteNote(DesktopShellViewModel shell, string title, string body)
    {
        shell.NewNoteCommand.Execute(null);
        Pump();
        shell.Notes.EditorText = body;
        Wait(shell.Notes.FlushAsync(FlushReason.NoteChange));
        Wait(shell.Notes.RenameAsync(shell.Notes.SelectedTab, title));
        Pump();
    }

    /// <summary>A note to seed. <see cref="DaysAgo"/> is 0 for today.</summary>
    private readonly record struct Entry(int DaysAgo, string Title, string Body);

    private static readonly Entry[] KoreanHistory =
    [
        new(7, "주간 회고", """
            이번 주에 끝낸 것
            - 온보딩 화면 세 가지 시안
            - 검색 속도 개선

            -[x] 회고 정리해서 공유
            """),
        new(5, "장보기", """
            -[x] 커피 원두
            -[x] 우유
            -[] 세제
            """),
        new(3, "읽을거리", """
            저장해 둔 링크들. #자료

            - 타이포그래피 기초
            - SQLite FTS5 트라이그램 토크나이저
            """),
        new(1, "면담 준비", """
            -[] 이력서 최신화
            -[] 포트폴리오 정리

            물어볼 것: 온보딩 일정, 팀 구성
            """),
    ];

    private static readonly Entry[] KoreanToday =
    [
        new(0, "3분기 계획 회의", """
            참석: 기획 2, 디자인 1, 개발 3

            -[x] 지난 분기 지표 정리
            -[] 예산안 초안 공유 @내일
            -[] 디자인 리뷰 일정 잡기
            -[] 온보딩 문구 최종본 넘기기

            논의한 것
            - 신규 온보딩 흐름을 9월 안에 내보내기로 했습니다. #출시
            - 검색은 한글 두 글자도 잡히게 하는 것이 먼저. 속도는 그다음.
            - 문서 정리는 다음 주로 미뤘습니다.

            정하지 못한 것
            - 가격 페이지 문구. 다음 회의에서.

            회의록 원본과 예산안 초안은 파일 탭에 붙여 두었습니다.
            """),
        new(0, "오늘 할 일", """
            -[] 회의록 정리해서 공유
            -[] 예산안 숫자 확인
            -[x] 스탠드업
            """),
    ];

    private static readonly Entry[] EnglishHistory =
    [
        new(7, "Weekly review", """
            Finished this week
            - Three onboarding mockups
            - Search speed

            -[x] Write the review up
            """),
        new(5, "Groceries", """
            -[x] Coffee beans
            -[x] Milk
            -[] Detergent
            """),
        new(3, "Reading list", """
            Links worth keeping. #reference

            - Typography basics
            - SQLite FTS5 trigram tokenizer
            """),
        new(1, "Interview prep", """
            -[] Update the CV
            -[] Tidy the portfolio

            Ask about: onboarding timeline, team shape
            """),
    ];

    private static readonly Entry[] EnglishToday =
    [
        new(0, "Q3 planning meeting", """
            Present: 2 product, 1 design, 3 engineering

            -[x] Pull last quarter's numbers
            -[] Share the draft budget @tomorrow
            -[] Book the design review
            -[] Hand over the final onboarding copy

            What we agreed
            - Ship the new onboarding flow within September. #release
            - Search should match two-character queries first; speed comes second.
            - Documentation cleanup moves to next week.

            Still open
            - Wording on the pricing page. Next meeting.

            The full minutes and the draft budget are in the Files tab.
            """),
        new(0, "Today", """
            -[] Write up the minutes
            -[] Check the budget figures
            -[x] Standup
            """),
    ];

    private static readonly string[] KoreanTags = ["회의", "3분기"];
    private static readonly string[] EnglishTags = ["meeting", "q3"];

    private static readonly (string Name, int Bytes)[] KoreanAttachments =
    [
        ("회의록.pdf", 184_320),
        ("예산안-초안.xlsx", 42_100),
    ];

    private static readonly (string Name, int Bytes)[] EnglishAttachments =
    [
        ("minutes.pdf", 184_320),
        ("draft-budget.xlsx", 42_100),
    ];

    /// <summary>Bytes that are stable per name, so a rerun shows the same sizes on the cards.</summary>
    private static byte[] Filler(string name, int length)
    {
        var content = new byte[length];
        int seed = name.Aggregate(17, static (acc, c) => (acc * 31) + c);
        for (int index = 0; index < length; index += 1)
        {
            content[index] = (byte)((seed + index) % 251);
        }

        return content;
    }

    private static void Shoot(Window window, string directory, string name)
    {
        window.UpdateLayout();
        Pump(window);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        using WriteableBitmap? bitmap = window.CaptureRenderedFrame();
        Assert.IsNotNull(bitmap, $"{name}: nothing rendered.");
        Assert.AreEqual(Width, bitmap.PixelSize.Width, $"{name}: wrong width for a Store image.");
        Assert.AreEqual(Height, bitmap.PixelSize.Height, $"{name}: wrong height for a Store image.");
        AssertNotBlank(bitmap, name);

        bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }

    /// <summary>
    /// Refuses to overwrite a good screenshot with an empty one.
    /// </summary>
    /// <remarks>
    /// The failure this guards against is not a crash: a frame captured before layout settles is a
    /// correctly sized rectangle of one colour, which saves without complaint and looks like a
    /// passing test until someone opens the file.
    /// </remarks>
    private static void AssertNotBlank(WriteableBitmap bitmap, string name)
    {
        using ILockedFramebuffer pixels = bitmap.Lock();
        var distinct = new HashSet<uint>();
        byte[] row = new byte[pixels.RowBytes];

        for (int y = 0; y < pixels.Size.Height; y += 1)
        {
            System.Runtime.InteropServices.Marshal.Copy(
                pixels.Address + (y * pixels.RowBytes), row, 0, row.Length);
            for (int x = 0; x < pixels.Size.Width; x += 1)
            {
                distinct.Add(BitConverter.ToUInt32(row, x * 4));
            }
        }

        Assert.IsGreaterThan(
            500,
            distinct.Count,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{name}: only {distinct.Count} distinct colours — the frame is blank or unlaid-out."));
    }

    private static void Wait(Task work)
    {
        for (int i = 0; i < 400 && !work.IsCompleted; i += 1)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }

        Assert.IsTrue(work.IsCompleted, "A seeding step never completed.");
        work.GetAwaiter().GetResult();
    }

    private static void Wait<T>(Task<T> work) => Wait((Task)work);

    private static void Pump(Window? window = null)
    {
        for (int i = 0; i < 20; i += 1)
        {
            Dispatcher.UIThread.RunJobs();
            window?.UpdateLayout();
            Thread.Sleep(5);
        }
    }
}
