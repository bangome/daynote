using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.App.Shell.Product;
using Daynote.Core.Agenda;
using Daynote.Core.Domain;
using Daynote.Core.Notes;
using Daynote.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Daynote.Desktop.Tests;

/// <summary>
/// Every section of Daynote Desktop B v2 over the design's own sample — eight notes today, the three
/// lists, the to-dos and events, three files on two days — at 1440×900, light and dark, written to
/// <c>frames/desktop-v2</c> (or <c>DAYNOTE_FRAMES_DIR</c>) to be laid beside the design.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class DesktopV2FrameTests
{
    private const int Width = 1440;
    private const int Height = 900;

    [TestMethod]
    public void Every_v2_section_renders_light_and_dark()
    {
        WithSample((window, shell) =>
        {
            foreach (bool dark in new[] { false, true })
            {
                string variant = dark ? "dark" : "light";
                shell.IsDark = dark;
                Wait(shell.Todo.RefreshAsync());

                shell.ShowEditorCommand.Execute(null);
                Shoot(window, $"v2-editor-{variant}");

                shell.ToggleTabMenuCommand.Execute(null);
                Assert.IsTrue(shell.IsTabMenuOpen);
                Shoot(window, $"v2-tabmenu-{variant}");
                shell.CloseTabMenuCommand.Execute(null);

                Wait(shell.ShowTimelineCommand.ExecuteAsync(null));
                PumpUntil(() => shell.TimelineGroups.Count > 0, "The timeline did not fold.");
                Shoot(window, $"v2-timeline-{variant}");

                foreach ((RightTab tab, string name) in new[]
                {
                    (RightTab.Todo, "todo"), (RightTab.Favorites, "favorites"), (RightTab.Tags, "tags"), (RightTab.Files, "files"),
                })
                {
                    if (tab == RightTab.Todo)
                    {
                        Wait(shell.ShowAllTodosCommand.ExecuteAsync(null));
                    }
                    else
                    {
                        Wait(shell.ShowListCommand.ExecuteAsync(tab));
                    }

                    if (tab == RightTab.Tags && shell.TagPanel.Tags.FirstOrDefault() is { } first)
                    {
                        first.IsExpanded = true;
                    }

                    Shoot(window, $"v2-list-{name}-{variant}");
                }

                Wait(shell.ShowAgendaListCommand.ExecuteAsync(shell.Todo.Lists.Single(list => list.Name == AgendaSeed.Work)));
                Shoot(window, $"v2-list-work-{variant}");
                Wait(shell.ShowAllTodosCommand.ExecuteAsync(null));

                shell.ShowEditorCommand.Execute(null);
                Wait(shell.AddDayTodoCommand.ExecuteAsync(null));
                shell.TodoEntry.Title = "회의자료 초안 공유";
                shell.TodoEntry.When = "내일 3시";
                Shoot(window, $"v2-create-{variant}");
                shell.CloseAddTodoCommand.Execute(null);

                Wait(shell.DayTodos.Single(row => row.Text == "릴리즈 노트 작성").JumpCommand.ExecuteAsync(null));
                Shoot(window, $"v2-edit-{variant}");
                shell.CloseAddTodoCommand.Execute(null);

                shell.OpenPaletteCommand.Execute(null);
                Shoot(window, $"v2-palette-{variant}");
                shell.Search.Query = "회의";
                PumpUntil(() => shell.Search.Results.Count > 0, "The palette found nothing.");
                Shoot(window, $"v2-palette-query-{variant}");
                shell.ClosePaletteCommand.Execute(null);

                shell.OpenSettingsCommand.Execute(null);
                Shoot(window, $"v2-settings-{variant}");
                shell.CloseSettingsCommand.Execute(null);

                shell.LeftCollapsed = true;
                shell.RightCollapsed = true;
                Shoot(window, $"v2-collapsed-{variant}");
                shell.LeftCollapsed = false;
                shell.RightCollapsed = false;
            }

            shell.IsDark = false;
        });
    }

    /// <summary>The design's <c>sampleData()</c>, on today, with the editor on the first note.</summary>
    internal static void WithSample(Action<Window, DesktopShellViewModel> body)
    {
        AppLanguage original = LocalizationService.Instance.Language;
        try
        {
            TestServices.WithInitialisedShell(Width, Height, (window, shell) =>
            {
                LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
                CultureInfo.CurrentCulture = LocalizationService.Instance.Culture;
                Seed(shell);
                Pump(window);
                body(window, shell);
            });
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(original);
        }
    }

    private static void Seed(DesktopShellViewModel shell)
    {
        LocalDate today = shell.SelectedDate;
        var agenda = TestServices.CurrentProvider!.GetRequiredService<IAgendaRepository>();
        Guid work = Wait(shell.Todo.CreateListAsync(AgendaSeed.Work)).Id;
        Guid life = Wait(shell.Todo.CreateListAsync(AgendaSeed.Life)).Id;
        Guid shopping = Wait(shell.Todo.CreateListAsync(AgendaSeed.Shopping)).Id;
        Wait(shell.Todo.SelectListAsync(shell.Todo.SelectedListId));
        foreach (AgendaItem item in AgendaSeed.Sample(LocalDates.ToDateOnly(today), work, life, shopping))
        {
            Wait(agenda.SaveAsync(item).AsTask());
        }

        var notes = new (int Offset, string Title, bool Favorite, string[] Tags, string Body)[]
        {
            (-6, "월간 보고 초안", true, ["보고"], "7월 월간 보고 핵심 요약\n\n- MAU 12.4만 (+8.2% MoM)\n- 신규 결제 전환율 3.1%"),
            (-4, "뉴스클리핑", false, ["뉴스클리핑"], "오늘의 업계 뉴스 정리\n\n- 관리단위 식별·연결 ID 플랫폼 사업 공고"),
            (-1, "고객 미팅 메모", false, ["고객"], "A사 정기 미팅\n\n- 대시보드 로딩 속도 개선 요청\n- 엑셀 내보내기 기능 문의 → 8월 로드맵 안내"),
            (2, "휴가 전 인수인계", false, ["일정"], "8월 첫째 주 휴가 전 정리할 것\n\n- 담당 이슈 재배정\n- 자동응답 설정"),
            (0, "주간회의 준비", true, ["회의", "기획"], "이번 주 주간회의 안건 정리\n\n1. 2분기 지표 리뷰\n2. 신규 기능 우선순위 논의\n3. 채용 진행 상황 공유"),
            (0, "배포 체크리스트", false, ["배포"], "v2.4.0 배포 전 확인사항\n\n※ 배포 후 30분간 에러 모니터링 필수"),
            (0, "디자인 리뷰 피드백", false, ["디자인"], "홈 화면 리뷰\n\n- 주간 스트립 대비 개선\n- 빈 상태 문구 다듬기"),
            (0, "점심 약속", false, [], "광화문 · 민수, 지현"),
            (0, "읽을거리", true, ["아카이브"], "- 로컬 퍼스트 소프트웨어 에세이\n- 타이포그래피 스케일 정리 글"),
            (0, "채용 인터뷰 질문", false, ["채용"], "1. 최근 해결한 성능 문제\n2. 디자인 시스템 경험"),
            (0, "분기 OKR 초안", false, ["기획"], "O. 기록 습관을 만든다\n- KR1 주 4회 이상 작성 사용자 30%"),
            (0, "퇴근 전 메모", false, [], "내일 아침 첫 일: 스테이징 로그 확인"),
        };

        foreach (var note in notes)
        {
            Wait(shell.SelectDateAsync(LocalDates.AddDays(today, note.Offset)));
            Wait(shell.NewNoteCommand.ExecuteAsync(null));
            shell.Notes.EditorText = note.Body;
            Wait(shell.Notes.FlushAsync(FlushReason.NoteChange));
            Wait(shell.Notes.RenameAsync(shell.Notes.SelectedTab, note.Title));
            foreach (string tag in note.Tags)
            {
                Wait(shell.Notes.AddTagAsync(shell.Notes.SelectedTab, tag));
            }

            if (note.Favorite)
            {
                Wait(shell.ToggleFavoriteCommand.ExecuteAsync(null));
            }
        }

        Wait(shell.SelectDateAsync(LocalDates.AddDays(today, -1)));
        using (var minutes = new MemoryStream(new byte[18_422]))
        {
            Wait(shell.Files.AddFromStreamAsync("A사_미팅녹취.txt", minutes));
        }

        Wait(shell.GoToTodayCommand.ExecuteAsync(null));
        foreach ((string name, int bytes) in new[] { ("요구사항_정의서_v3.docx", 482_134), ("화면설계_홈.png", 1_204_833) })
        {
            using var content = new MemoryStream(new byte[bytes]);
            Wait(shell.Files.AddFromStreamAsync(name, content));
        }

        Wait(shell.SelectDayNoteCommand.ExecuteAsync(shell.Notes.Tabs.First(tab => !tab.IsProjection)));
        Wait(shell.Todo.RefreshAsync());
        Wait(shell.Favorites.RefreshAsync());
        Wait(shell.TagPanel.RefreshAsync());
        Wait(shell.Week.RefreshAsync());
        Wait(shell.Calendar.LoadAsync());
        Pump();
    }

    internal static void Shoot(Window window, string name)
    {
        Pump(window);
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Pump(window);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using WriteableBitmap? bitmap = window.CaptureRenderedFrame();
        Assert.IsNotNull(bitmap, $"{name}: nothing rendered.");
        Assert.AreEqual(Width, bitmap.PixelSize.Width, $"{name}: wrong width.");
        string directory = Environment.GetEnvironmentVariable("DAYNOTE_FRAMES_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "frames", "desktop-v2");
        Directory.CreateDirectory(directory);
        bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }

    internal static void Wait(Task work)
    {
        for (int i = 0; i < 400 && !work.IsCompleted; i += 1)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }

        Assert.IsTrue(work.IsCompleted, "A step never completed.");
        work.GetAwaiter().GetResult();
    }

    internal static T Wait<T>(Task<T> work)
    {
        Wait((Task)work);
        return work.Result;
    }

    internal static void PumpUntil(Func<bool> settled, string because)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!settled())
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, because);
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
    }

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
