using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.Core.Domain;
using Daynote.Core.Notes;
using Daynote.Mobile.ViewModels;
using Daynote.Mobile.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// The App Store and Google Play listing images, rendered from the real phone UI at the exact pixel
/// sizes each store asks for.
/// </summary>
/// <remarks>
/// <para>
/// Opt-in (<c>DAYNOTE_STORE_SHOTS=1</c>), unlike every other test here: the output is committed under
/// docs/brand, and a suite that rewrote those files on each run would leave a dirty tree behind for
/// nothing, which is what the desktop's store shots do.
/// </para>
/// <para>
/// The screen is laid out at the phone's logical size and the whole view scaled up as vectors, so a
/// 1320-pixel-wide image is drawn at 1320 pixels rather than enlarged from 390. The headless platform
/// renders at a scaling of 1 and has no other way to reach a retina density.
/// </para>
/// </remarks>
[TestClass]
public sealed class StoreScreenshotTests
{
    private static readonly string BrandRoot =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "brand");

    /// <summary>iPhone 6.9" display, the size App Store Connect requires.</summary>
    private static readonly Target AppStore = new("app-store", 1320, 2868, 390, new Thickness(0, 59, 0, 34));

    /// <summary>
    /// iPad 13", landscape: 1376 by 1032 points at 2x, wide enough for the Tablet layout (sidebar,
    /// day and day panel), where portrait's 1032 would fold the sidebar away.
    /// </summary>
    private static readonly Target AppStoreIpad13 = new("app-store", 2752, 2064, 1376, new Thickness(0, 24, 0, 20), "ipad-13", Subfolder: true);

    /// <summary>Google Play phone: 9:18, the tallest ratio Play accepts (its limit is 2:1).</summary>
    private static readonly Target GooglePlay = new("google-play", 1080, 2160, 360, new Thickness(0, 24, 0, 24));

    /// <summary>Google Play 7-inch tablet, portrait: 600 points wide at 2x.</summary>
    private static readonly Target PlayTablet7 = new("google-play", 1200, 1920, 600, new Thickness(0, 24, 0, 24), "tablet7");

    /// <summary>Google Play 10-inch tablet, portrait: 800 points wide at 2x.</summary>
    private static readonly Target PlayTablet10 = new("google-play", 1600, 2560, 800, new Thickness(0, 24, 0, 24), "tablet10");

    [TestMethod]
    [DataRow("ko")]
    [DataRow("en")]
    public void App_Store_images(string language) => Render(AppStore, language);

    [TestMethod]
    [DataRow("ko")]
    [DataRow("en")]
    public void App_Store_iPad_images(string language) => Render(AppStoreIpad13, language);

    [TestMethod]
    [DataRow("ko")]
    [DataRow("en")]
    public void Google_Play_images(string language) => Render(GooglePlay, language);

    [TestMethod]
    [DataRow("ko")]
    [DataRow("en")]
    public void Google_Play_tablet_images(string language)
    {
        Render(PlayTablet7, language);
        Render(PlayTablet10, language);
    }

    private static void Render(Target target, string languageCode)
    {
        if (Environment.GetEnvironmentVariable("DAYNOTE_STORE_SHOTS") != "1")
        {
            Assert.Inconclusive("Store images are rendered on request: set DAYNOTE_STORE_SHOTS=1.");
        }

        AppLanguage language = languageCode == "ko" ? AppLanguage.Korean : AppLanguage.English;
        string directory = target.Subfolder
            ? Path.Combine(BrandRoot, target.Folder, languageCode, target.Variant!)
            : Path.Combine(BrandRoot, target.Folder, languageCode);
        Directory.CreateDirectory(directory);
        string root = Path.Combine(Path.GetTempPath(), "daynote-store-shots", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        HeadlessAppFixture.OnUiThread(() =>
        {
            AppLanguage original = LocalizationService.Instance.Language;
            ThemeVariantHolder theme = new(Application.Current!.RequestedThemeVariant);
            ServiceProvider provider = TestServices.Build(root, Application.Current!);
            try
            {
                LocalizationService.Instance.SetLanguage(language);
                Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;

                var shell = provider.GetRequiredService<MobileShellViewModel>();
                double scale = (double)target.Width / target.LogicalWidth;
                var view = new MainView
                {
                    DataContext = shell,
                    Width = target.LogicalWidth,
                    Height = target.Height / scale,

                    // The status bar and the home indicator a real screen has, so the tab bar sits
                    // where it does on a device rather than on the image's bottom edge.
                    PreviewSafeArea = target.SafeArea,
                };
                var host = new Window
                {
                    Width = target.Width,
                    Height = target.Height,
                    Content = new LayoutTransformControl
                    {
                        LayoutTransform = new ScaleTransform(scale, scale),
                        Child = view,
                    },
                };
                host.Show();

                Wait(shell.InitializeAsync());
                Seed(shell, language);
                ScreenshotTests.CaptureSeededTodos(provider);
                if (target.LogicalWidth >= MobileLayouts.TabletWidth)
                {
                    // The Tablet layout shows the day's files under its notes, where a phone's
                    // first screen never reaches.
                    SeedFiles(shell, language);
                }

                Wait(shell.RefreshAllAsync());

                string prefix = target.Variant is { } variant
                    ? $"daynote-{target.Folder}-{variant}-{languageCode}"
                    : $"daynote-{target.Folder}-{languageCode}";

                // 1: the day, which is what opening the app shows.
                shell.GoToPageCommand.Execute(MobilePage.Day);
                Capture(host, directory, $"{prefix}-01-day");

                // 2: the meeting note open, checkboxes and tags in view.
                Wait(shell.Notes.SelectNoteAsync(shell.Notes.Tabs[0]));
                shell.IsEditorOpen = true;
                Capture(host, directory, $"{prefix}-02-note");
                shell.IsEditorOpen = false;

                // 3: every open to-do across the month.
                shell.GoToPageCommand.Execute(MobilePage.Lists);
                Capture(host, directory, $"{prefix}-03-todos");

                // 4: search.
                shell.GoToPageCommand.Execute(MobilePage.Search);
                shell.Search.Query = language == AppLanguage.Korean ? "회의" : "meeting";
                PumpUntil(() => shell.Search.Results.Count > 0);
                Capture(host, directory, $"{prefix}-04-search");

                // 5: the day again, dark.
                shell.Search.Query = string.Empty;
                shell.GoToPageCommand.Execute(MobilePage.Day);
                Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
                shell.IsDark = true;
                Capture(host, directory, $"{prefix}-05-dark");

                host.Close();
            }
            finally
            {
                LocalizationService.Instance.SetLanguage(original);
                Application.Current!.RequestedThemeVariant = theme.Value;
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });

        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static void Seed(MobileShellViewModel shell, AppLanguage language)
    {
        bool korean = language == AppLanguage.Korean;
        LocalDate today = LocalDates.FromDateOnly(DateOnly.FromDateTime(DateTime.Now));

        // Earlier days first, so the calendar shows a month with a history in it.
        foreach (Entry entry in korean ? KoreanHistory : EnglishHistory)
        {
            Wait(shell.SelectDateAsync(LocalDates.AddDays(today, -entry.DaysAgo)));
            WriteNote(shell, entry.Title, entry.Body);
        }

        Wait(shell.SelectDateAsync(today));
        foreach (Entry entry in korean ? KoreanToday : EnglishToday)
        {
            WriteNote(shell, entry.Title, entry.Body);
        }

        Wait(shell.Notes.SelectNoteAsync(shell.Notes.Tabs[0]));
        foreach (string tag in korean ? ["회의", "3분기"] : new[] { "meeting", "Q3" })
        {
            shell.TagInput = tag;
            Wait(shell.CommitTagCommand.ExecuteAsync(null));
        }

        Wait(shell.RefreshAllAsync());
        Pump();
    }

    private static void SeedFiles(MobileShellViewModel shell, AppLanguage language)
    {
        bool korean = language == AppLanguage.Korean;
        Wait(AddFile(shell, korean ? "회의실 화이트보드.png" : "Whiteboard.png", ScreenshotTests.SamplePng(320, 240)));
        Wait(AddFile(shell, korean ? "3분기 예산안.pdf" : "Q3 budget draft.pdf", new byte[184_320]));
        Wait(shell.Files.RefreshAsync());
    }

    private static async Task AddFile(MobileShellViewModel shell, string name, byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        await shell.Files.AddFromStreamAsync(name, stream);
    }

    private static void WriteNote(MobileShellViewModel shell, string title, string body)
    {
        Wait(shell.NewNoteCommand.ExecuteAsync(null));
        shell.Notes.EditorText = body;
        Wait(shell.Notes.FlushAsync(FlushReason.NoteChange));
        Wait(shell.Notes.RenameAsync(shell.Notes.SelectedTab, title));
        shell.IsEditorOpen = false;
        Pump();
    }

    private static void Capture(Window host, string directory, string name)
    {
        Pump();
        host.UpdateLayout();
        Pump();
        using Avalonia.Media.Imaging.WriteableBitmap? frame = host.CaptureRenderedFrame();
        Assert.IsNotNull(frame, $"No frame for {name}.");
        Assert.AreEqual(host.Width, frame.PixelSize.Width, $"{name} is not the store's width.");
        Assert.AreEqual(host.Height, frame.PixelSize.Height, $"{name} is not the store's height.");
        frame.Save(Path.Combine(directory, $"{name}.png"), new PngBitmapEncoderOptions());
    }

    private static void Wait(Task task)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted)
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, "A seeding step did not finish.");
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        task.GetAwaiter().GetResult();
        Pump();
    }

    private static void PumpUntil(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, "The screen never reached the state to capture.");
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }

    private sealed record Target(string Folder, int Width, int Height, double LogicalWidth, Thickness SafeArea, string? Variant = null, bool Subfolder = false);

    private sealed record ThemeVariantHolder(Avalonia.Styling.ThemeVariant? Value);

    private readonly record struct Entry(int DaysAgo, string Title, string Body);

    private static readonly Entry[] KoreanHistory =
    [
        new(9, "주간 회고", "이번 주에 끝낸 것\n- 온보딩 화면 세 가지 시안\n- 검색 속도 개선\n\n-[x] 회고 정리해서 공유"),
        new(6, "장보기", "-[x] 커피 원두\n-[x] 우유\n-[] 세제"),
        new(4, "읽을거리", "저장해 둔 링크들\n\n- 타이포그래피 기초\n- 좋은 회의록 쓰는 법"),
        new(2, "면담 준비", "-[] 이력서 최신화\n-[] 포트폴리오 정리\n\n물어볼 것: 온보딩 일정, 팀 구성"),
        new(1, "운동 기록", "러닝 5km · 28분\n다음 주 목표: 주 3회"),
    ];

    private static readonly Entry[] KoreanToday =
    [
        new(0, "3분기 계획 회의", "참석: 기획 2, 디자인 1, 개발 3\n\n-[x] 지난 분기 지표 정리\n-[] 예산안 초안 공유\n-[] 디자인 리뷰 일정 잡기\n\n논의한 것\n- 신규 온보딩 흐름을 이달 안에 내보내기로 했습니다.\n- 검색은 두 글자로도 찾히게 하는 것이 먼저."),
        new(0, "오늘 할 일", "-[] 회의록 정리해서 공유\n-[] 예산안 숫자 확인\n-[x] 스탠드업"),
    ];

    private static readonly Entry[] EnglishHistory =
    [
        new(9, "Weekly review", "Finished this week\n- Three onboarding mockups\n- Faster search\n\n-[x] Share the review"),
        new(6, "Groceries", "-[x] Coffee beans\n-[x] Milk\n-[] Detergent"),
        new(4, "Reading list", "Saved links\n\n- Typography basics\n- How to write good meeting notes"),
        new(2, "Interview prep", "-[] Update my résumé\n-[] Tidy the portfolio\n\nTo ask: onboarding timeline, team size"),
        new(1, "Workout log", "Run 5 km · 28 min\nNext week: three times"),
    ];

    private static readonly Entry[] EnglishToday =
    [
        new(0, "Q3 planning meeting", "Attending: product 2, design 1, engineering 3\n\n-[x] Last quarter's numbers\n-[] Share the budget draft\n-[] Book the design review\n\nDecided\n- Ship the new onboarding flow this month.\n- Search first has to find two-letter words."),
        new(0, "Today", "-[] Send the meeting notes\n-[] Check the budget figures\n-[x] Stand-up"),
    ];
}
