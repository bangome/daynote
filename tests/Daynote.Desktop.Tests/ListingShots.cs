using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Daynote.App.Account;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.App.Shell.Product;
using Daynote.App.Tests.Account;
using Daynote.Core.Domain;
using Daynote.Core.Notes;
using Daynote.Core.Sync;
using Daynote.Desktop.ViewModels;
using Daynote.Desktop.Views;

namespace Daynote.Desktop.Tests;

/// <summary>
/// The parts the Store listing images and the website images both need: a shell seeded with a
/// month of real notes, and a capture that refuses to write a blank frame.
/// </summary>
/// <remarks>
/// This used to live inside <see cref="StoreScreenshotTests"/>. The website's screenshots under
/// <c>cloud/site/public/img/</c> had rotted the same way the Store's did — on 2026-10-01 the hero
/// still showed the clipboard drawer, deleted in August — and the fix is the same one: render them
/// from the shipping shell. Two callers rendering from one seed is the whole point; a second copy
/// of the seed data would drift from the first.
/// </remarks>
internal static class ListingShots
{
    /// <summary>The catalogue the worker sells, so the table prices both tiers rather than a dash.</summary>
    private static readonly BillingOffer[] Offers =
    [
        new(BillingTier.Pro, BillingPlan.Monthly, [new Money("KRW", 2_900), new Money("USD", 249)]),
        new(BillingTier.Pro, BillingPlan.Annual, [new Money("KRW", 24_000), new Money("USD", 1_999)]),
        new(BillingTier.Premium, BillingPlan.Monthly, [new Money("KRW", 5_900), new Money("USD", 499)]),
        new(BillingTier.Premium, BillingPlan.Annual, [new Money("KRW", 48_000), new Money("USD", 3_999)]),
    ];

    /// <summary>
    /// Signs an account in on a trial and opens the settings page at the plan table.
    /// </summary>
    /// <remarks>
    /// A trial with everything on sale is the state that shows the most: the free column, both paid
    /// columns priced, and the button that buys one. A signed-out shell would show an empty page
    /// and a paid one would hide half the table behind "current plan".
    /// </remarks>
    internal static void ShowPlans(Window window, DesktopShellViewModel shell)
    {
        var accounts = new FakeAccounts
        {
            Email = "jiwon@example.com",
            Billing = new BillingLinks(true, false, Offers: Offers),
            Entitlement = new Entitlement(
                EntitlementState.Trial,
                DateTimeOffset.UtcNow.AddDays(3).AddHours(1),
                true,
                false,
                BillingTier.Pro,
                null,
                2L << 30,
                0),
        };

        var account = new AccountViewModel(
            accounts.Service,
            accounts.Store,
            () => ValueTask.FromResult(SyncReport.For(SyncOutcome.Completed)),
            new NoStoreExport(),
            _ => { },
            Path.Combine(Path.GetTempPath(), "daynote-store-shots-conflicts"));
        Wait(account.SignInCommand.ExecuteAsync(null));
        shell.Account = account;
        Pump();

        shell.SettingsViewModel!.Section = SettingsSection.Account;
        shell.OpenSettingsCommand.Execute(null);
        Pump();
        window.UpdateLayout();

        // The table is below the fold of a 560px dialog; the listing wants the table, not the
        // account row above it.
        ScrollViewer scroller = window.GetVisualDescendants().OfType<SettingsPanel>().Single()
            .GetVisualDescendants().OfType<ScrollViewer>().First();
        Control table = scroller.GetVisualDescendants().OfType<Control>()
            .First(control => control.Name == "PlanTable");
        Assert.IsTrue(table.IsEffectivelyVisible, "The plan table is not on the page.");
        scroller.Offset = new Avalonia.Vector(0, scroller.Extent.Height);
        Pump();
        window.UpdateLayout();
    }

    /// <summary>The account view model wants an exporter; nothing is exported for a screenshot.</summary>
    private sealed class NoStoreExport : IRecoveryKeyExporter
    {
        public Task<bool> TryCopyToClipboardAsync(string recoveryKey) => Task.FromResult(false);

        public Task<bool> TrySaveToFileAsync(string recoveryKey) => Task.FromResult(false);
    }

    internal static void Seed(DesktopShellViewModel shell, AppLanguage language)
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

    internal static void Shoot(Window window, string directory, string name, int width, int height)
    {
        window.UpdateLayout();
        Pump(window);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        using WriteableBitmap? bitmap = window.CaptureRenderedFrame();
        Assert.IsNotNull(bitmap, $"{name}: nothing rendered.");
        Assert.AreEqual(width, bitmap.PixelSize.Width, $"{name}: wrong width.");
        Assert.AreEqual(height, bitmap.PixelSize.Height, $"{name}: wrong height.");
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

    internal static void Wait(Task work)
    {
        for (int i = 0; i < 400 && !work.IsCompleted; i += 1)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }

        Assert.IsTrue(work.IsCompleted, "A seeding step never completed.");
        work.GetAwaiter().GetResult();
    }

    internal static void Wait<T>(Task<T> work) => Wait((Task)work);

    internal static void Pump(Window? window = null)
    {
        for (int i = 0; i < 20; i += 1)
        {
            Dispatcher.UIThread.RunJobs();
            window?.UpdateLayout();
            Thread.Sleep(5);
        }
    }
}
