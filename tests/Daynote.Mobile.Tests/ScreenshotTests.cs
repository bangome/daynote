using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Daynote.App.Composition;
using Daynote.Core.Domain;
using Daynote.Core.Notes;
using Daynote.Mobile.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// Renders each phone screen to a PNG, at handset size, in both themes, with the notes the mobile
/// design's own prototype shows.
/// </summary>
/// <remarks>
/// These are not assertions about pixels; they exist so a layout can be looked at without an
/// emulator and set beside the design, which is the only way to catch what a binding test cannot -
/// a tab bar that collides with the home indicator, a day cell too small to hit, text that wraps to
/// three lines. The view is laid out against a notched phone's safe area (56 points above, 34
/// below) so the frame lines up with the design's. The files land under
/// <c>artifacts/mobile-screens</c> and are not compared to anything.
/// </remarks>
[TestClass]
public sealed class ScreenshotTests
{
    private static readonly string OutputDirectory =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "mobile-screens");

    [TestMethod]
    [DataRow("Light")]
    [DataRow("Dark")]
    public void Every_screen_renders(string variantName)
    {
        Directory.CreateDirectory(OutputDirectory);

        TestServices.WithInitialisedShell((view, shell) =>
        {
            // Through the shell, as the settings pills do, so the pills show the theme on screen.
            shell.IsDark = variantName == "Dark";
            view.PreviewSafeArea = new Thickness(0, 56, 0, 34);

            LocalDate today = LocalDates.FromDateOnly(DateOnly.FromDateTime(DateTime.Now));
            Seed(shell, today);
            string suffix = variantName.ToLowerInvariant();

            shell.GoToPageCommand.Execute(MobilePage.Day);
            Capture(view, $"day-{suffix}");

            // The meeting note, which carries a star, two tags and three to-dos.
            Pump(() => shell.Notes.SelectNoteAsync(shell.Notes.Tabs.First(t => t.Title == "주간회의 준비")));
            shell.IsEditorOpen = true;
            Capture(view, $"editor-{suffix}");
            shell.IsEditorOpen = false;

            shell.GoToPageCommand.Execute(MobilePage.Lists);
            shell.SelectListCommand.Execute(Daynote.App.Shell.Product.RightTab.Todo);
            Capture(view, $"lists-{suffix}");
            shell.SelectListCommand.Execute(Daynote.App.Shell.Product.RightTab.Favorites);
            Capture(view, $"lists-fav-{suffix}");
            shell.SelectListCommand.Execute(Daynote.App.Shell.Product.RightTab.Tags);
            shell.TagChips.First(chip => chip.Name == "회의").SelectCommand.Execute(null);
            Capture(view, $"lists-tags-{suffix}");
            shell.SelectListCommand.Execute(Daynote.App.Shell.Product.RightTab.Todo);

            shell.GoToPageCommand.Execute(MobilePage.Search);
            shell.RecentSearches.Clear();
            shell.RecentSearches.Add("회의");
            shell.RecentSearches.Add("배포 체크");
            Capture(view, $"search-{suffix}");
            shell.Search.Query = "회의";
            Pump(() => shell.Search.SearchNowAsync("회의"));
            Capture(view, $"search-q-{suffix}");
            shell.Search.Query = string.Empty;

            shell.GoToPageCommand.Execute(MobilePage.Settings);
            Capture(view, $"settings-{suffix}");

            shell.GoToPageCommand.Execute(MobilePage.Day);
            shell.OpenMonthPickerCommand.Execute(null);
            Capture(view, $"sheet-{suffix}");
            shell.CloseMonthPickerCommand.Execute(null);

            Pump(() => shell.SelectDateAsync(LocalDates.AddDays(today, 3)));
            Capture(view, $"day-empty-{suffix}");
            Pump(() => shell.SelectDateAsync(today));
            shell.IsDark = false;
        });
    }

    /// <summary>The prototype's six notes, on the same days around today.</summary>
    private static void Seed(MobileShellViewModel shell, LocalDate today)
    {
        string Md(int offset)
        {
            LocalDate day = LocalDates.AddDays(today, offset);
            return string.Create(CultureInfo.InvariantCulture, $"{day.Month}/{day.Day}");
        }

        (int Offset, string Title, bool Favorite, string[] Tags, string Body)[] notes =
        [
            (-6, "월간 보고 초안", true, ["보고"], "월간 보고 핵심 요약\n\n- MAU 12.4만 (+8.2% MoM)\n- 신규 결제 전환율 3.1%"),
            (-4, "뉴스클리핑", false, ["뉴스클리핑"], "오늘의 업계 뉴스 정리\n\n- 모바일 순찰관리 서비스 사례\n- 현장근무자용 업무 플랫폼 동향"),
            (-1, "고객 미팅 메모", false, ["고객"], $"A사 정기 미팅\n\n- 대시보드 로딩 속도 개선 요청\n- 엑셀 내보내기 기능 문의\n\n-[] 속도 개선 이슈 티켓 생성 ({Md(-1)} 12:00)"),
            (2, "휴가 전 인수인계", false, ["일정"], $"휴가 전 정리할 것\n\n-[] 담당 이슈 재배정 ({Md(2)} 18:00)\n-[] 자동응답 설정"),
            (0, "주간회의 준비", true, ["회의", "기획"], $"이번 주 주간회의 안건 정리\n\n1. 2분기 지표 리뷰\n2. 신규 기능 우선순위 논의\n\n-[] 회의자료 초안 공유 ({Md(1)} 10:00)\n-[] 회의실 예약 확인 ({Md(0)} 17:00)\n-[x] 지난주 액션아이템 정리"),
            (0, "배포 체크리스트", false, ["배포"], $"v2.4.0 배포 전 확인사항\n\n-[] 스테이징 회귀 테스트 ({Md(2)} 14:00)\n-[] 릴리즈 노트 작성\n-[x] DB 마이그레이션 스크립트 리뷰"),
        ];

        foreach ((int offset, string title, bool favorite, string[] tags, string body) in notes)
        {
            Pump(() => shell.SelectDateAsync(LocalDates.AddDays(today, offset)));
            Pump(() => shell.NewNoteCommand.ExecuteAsync(null));
            shell.Notes.EditorText = body;
            Pump(() => shell.Notes.FlushAsync(FlushReason.NoteChange));
            Pump(() => shell.Notes.RenameAsync(shell.Notes.SelectedTab!, title));
            foreach (string tag in tags)
            {
                shell.TagInput = tag;
                Pump(() => shell.CommitTagCommand.ExecuteAsync(null));
            }

            if (favorite)
            {
                Pump(() => shell.ToggleFavoriteCommand.ExecuteAsync(null));
            }

            Pump(() => shell.CloseEditorAsync());
        }

        Pump(() => shell.SelectDateAsync(today));
        Pump(() => shell.RefreshAllAsync());
    }

    /// <summary>Runs an async command to completion on the dispatcher the UI is on.</summary>
    private static void Pump(Func<Task> work)
    {
        Task task = work();
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted)
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, "The seeding step did not complete within 20 seconds.");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        task.GetAwaiter().GetResult();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Control view, string name)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        TopLevel.GetTopLevel(view)?.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        if ((TopLevel.GetTopLevel(view) as Window)?.CaptureRenderedFrame() is not { } frame)
        {
            Assert.Fail($"The headless platform rendered no frame for {name}.");
            return;
        }

        using (frame)
        {
            string path = Path.Combine(OutputDirectory, $"{name}.png");
            frame.Save(path, new PngBitmapEncoderOptions());
            Assert.IsGreaterThan(0, new FileInfo(path).Length, $"{name}.png is empty.");
        }
    }
}
