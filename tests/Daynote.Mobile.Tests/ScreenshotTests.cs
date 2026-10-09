using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Daynote.App.Composition;
using Daynote.Core.Agenda;
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
            SeedFiles(shell);
            string suffix = variantName.ToLowerInvariant();

            shell.GoToPageCommand.Execute(MobilePage.Day);
            Capture(view, $"day-{suffix}");

            // The files section, scrolled into view under the to-dos.
            ScrollViewer dayScroller = view.GetVisualDescendants().OfType<Views.DayPage>().Single()
                .GetVisualDescendants().OfType<ScrollViewer>().First();
            dayScroller.ScrollToEnd();
            Capture(view, $"day-files-{suffix}");
            dayScroller.ScrollToHome();

            shell.OpenAttachSheetCommand.Execute(null);
            Capture(view, $"attach-sheet-{suffix}");
            shell.CloseAttachSheetCommand.Execute(null);

            shell.DayFiles[0].ShowMenuCommand.Execute(null);
            Capture(view, $"file-menu-{suffix}");
            shell.RequestDeleteFileCommand.Execute(null);
            Capture(view, $"file-confirm-{suffix}");
            shell.CloseFileMenuCommand.Execute(null);

            Pump(() => shell.DayFiles.First(row => row.Item.IsImage).OpenCommand.ExecuteAsync(null));
            Capture(view, $"viewer-{suffix}");
            shell.CloseImageViewerCommand.Execute(null);

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
    internal static void Seed(MobileShellViewModel shell, LocalDate today)
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

        CaptureSeededTodos();

        Pump(() => shell.SelectDateAsync(today));
        Pump(() => shell.RefreshAllAsync());
    }

    /// <summary>
    /// Turns the <c>-[]</c> lines in the seeded bodies above into to-do entities.
    /// </summary>
    /// <remarks>
    /// The bodies stay written as checkboxes because that is what makes the fixture readable, but
    /// the panels read entities now, so something has to do the conversion. This is the §8
    /// migration's own walk — it cannot do it itself, because it runs when the database is opened
    /// and these notes are written afterwards.
    /// </remarks>
    internal static void CaptureSeededTodos(IServiceProvider? services = null)
    {
        var provider = services ?? TestServices.CurrentProvider
            ?? throw new InvalidOperationException("No shell is running.");
        var notes = (INoteRepository)provider.GetService(typeof(INoteRepository))!;
        var agenda = (IAgendaRepository)provider.GetService(typeof(IAgendaRepository))!;

        Pump(async () =>
        {
            foreach (NoteSummary note in await notes.GetAllNotesAsync())
            {
                foreach (ScannedTodo todo in TodoBodyScan.Scan(note.Id, note.LocalDate, note.Body))
                {
                    await agenda.SaveAsync(new AgendaItem(
                        todo.Id,
                        AgendaList.DefaultId,
                        AgendaKind.Task,
                        todo.Text,
                        string.Empty,
                        "Asia/Seoul",
                        StartsAt: null,
                        EndsAt: null,
                        DueAt: todo.DueAt ?? new WallClock(
                            new DateTime(note.LocalDate.Year, note.LocalDate.Month, note.LocalDate.Day, 0, 0, 0)),
                        HasDueTime: todo.HasDueTime,
                        Rrule: null,
                        SeriesId: null,
                        RecurrenceId: null,
                        todo.Completed ? AgendaStatus.Completed : AgendaStatus.NeedsAction,
                        CompletedUtc: todo.Completed ? DateTimeOffset.UtcNow : null,
                        Priority: 0,
                        TimelineVisibility.Auto,
                        SourceNoteId: note.Id,
                        ExceptionDates: [],
                        // As the migration does it: a line with a stamp reminded, one without
                        // never did.
                        todo.DueAt is null ? AgendaAlert.None : AgendaAlert.Default,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow));
                }
            }
        });
    }

    /// <summary>
    /// Three attachments on the selected day: a photo, a document, and one whose bytes are still to
    /// come from another device (its row is there and the asset is not).
    /// </summary>
    internal static void SeedFiles(MobileShellViewModel shell)
    {
        string dataRoot = TestServices.CurrentDataRoot ?? throw new InvalidOperationException("No shell is running.");
        Pump(async () =>
        {
            using (var photo = new MemoryStream(SamplePng(320, 240)))
            {
                await shell.Files.AddFromStreamAsync("회의실 화이트보드.png", photo);
            }

            using (var document = new MemoryStream(new byte[184_320]))
            {
                await shell.Files.AddFromStreamAsync("2분기 지표 리뷰.pdf", document);
            }

            using var pending = new MemoryStream(new byte[2_516_582]);
            if (await shell.Files.AddFromStreamAsync("현장 사진 모음.zip", pending) is { } missing)
            {
                File.Delete(Path.Combine(dataRoot, "files", missing.RelativePath));
            }

            await shell.Files.RefreshAsync();
        });
    }

    /// <summary>A PNG of a sun over a navy ground, the brand's two colours, so a thumbnail reads as one.</summary>
    internal static byte[] SamplePng(int width, int height)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        using (Avalonia.Media.DrawingContext context = bitmap.CreateDrawingContext())
        {
            context.FillRectangle(new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1b2356")), new Rect(0, 0, width, height));
            context.DrawEllipse(new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#ee7f35")), null,
                new Point(width * 0.62, height * 0.42), height * 0.22, height * 0.22);
            context.FillRectangle(new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#f6f5f1")),
                new Rect(0, height * 0.72, width, height * 0.28));
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        return stream.ToArray();
    }

    /// <summary>Runs an async command to completion on the dispatcher the UI is on.</summary>
    internal static void Pump(Func<Task> work)
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
