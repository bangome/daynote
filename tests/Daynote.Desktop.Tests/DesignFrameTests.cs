using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.App.Shell.Product;
using Daynote.Core.Domain;
using Daynote.Core.Notes;
using Daynote.Desktop.ViewModels;
using Daynote.Desktop.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Desktop.Tests;

/// <summary>
/// The shell in each state the desktop design draws, over the design's own sample notes, written to
/// <c>frames/design-*.png</c> at the size the design is reviewed at.
/// </summary>
/// <remarks>
/// These are the frames a person lays beside the prototype (.omc/design/Daynote Desktop B.dc.html)
/// to check the port: the same six notes, the same two files, the same day selected. Like
/// <see cref="RenderedFrameTests"/> the assertions only prove the evidence is real — each frame is the
/// size asked for and has something on it — and the view-model state each one claims to show.
/// </remarks>
[TestClass]
public sealed class DesignFrameTests
{
    private const int Width = 1440;
    private const int Height = 900;

    private static readonly string FramesDirectory = Path.Combine(AppContext.BaseDirectory, "frames");

    [TestMethod]
    public void The_design_states_render_over_the_sample_notes()
    {
        AppLanguage original = LocalizationService.Instance.Language;
        try
        {
            TestServices.WithInitialisedShell(Width, Height, (window, shell) =>
            {
                LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
                Pump(window);
                Seed(shell);

                foreach (bool dark in new[] { false, true })
                {
                    string variant = dark ? "dark" : "light";
                    shell.IsDark = dark;

                    Show(shell, s => s.ShowEditorCommand.Execute(null));
                    Assert.IsTrue(shell.IsEditorMode);
                    Shoot(window, $"design-editor-{variant}");

                    Show(shell, s => Wait(s.ShowTimelineCommand.ExecuteAsync(null)));
                    Assert.IsTrue(shell.IsTimelineMode);
                    Shoot(window, $"design-timeline-{variant}");

                    Show(shell, s => s.ShowEditorCommand.Execute(null));
                    shell.OpenPaletteCommand.Execute(null);
                    Pump(window);
                    Shoot(window, $"design-palette-{variant}");

                    shell.Search.Query = "회의";
                    for (int i = 0; i < 60 && shell.Search.Results.Count == 0; i++)
                    {
                        Pump(window);
                    }

                    Shoot(window, $"design-palette-query-{variant}");
                    shell.ClosePaletteCommand.Execute(null);

                    shell.OpenSettingsCommand.Execute(null);
                    Pump(window);
                    Shoot(window, $"design-settings-{variant}");
                    shell.SettingsViewModel!.Section = SettingsSection.Shortcuts;
                    Shoot(window, $"design-settings-shortcuts-{variant}");
                    shell.SettingsViewModel.Section = SettingsSection.General;
                    shell.CloseSettingsCommand.Execute(null);

                    foreach ((RightTab tab, string name) in new[]
                    {
                        (RightTab.Todo, "todo"), (RightTab.Favorites, "favorites"), (RightTab.Tags, "tags"), (RightTab.Files, "files"),
                    })
                    {
                        Show(shell, s => Wait(s.ShowListCommand.ExecuteAsync(tab)));
                        Assert.IsTrue(shell.IsListMode);
                        if (tab == RightTab.Tags && shell.TagPanel.Tags.FirstOrDefault() is { } first)
                        {
                            first.IsExpanded = true;
                        }

                        Shoot(window, $"design-list-{name}-{variant}");
                    }

                    Show(shell, s => s.ShowEditorCommand.Execute(null));
                    Wait(shell.SelectDateAsync(LocalDates.AddDays(shell.SelectedDate, 1)));
                    Pump(window);
                    Assert.IsTrue(shell.IsDayEmpty, "Tomorrow was seeded; the empty-day frame needs an empty day.");
                    Shoot(window, $"design-empty-day-{variant}");
                    Wait(shell.GoToTodayCommand.ExecuteAsync(null));

                    shell.LeftCollapsed = true;
                    shell.RightCollapsed = true;
                    Shoot(window, $"design-collapsed-{variant}");
                    shell.LeftCollapsed = false;
                    shell.RightCollapsed = false;
                }

                shell.IsDark = false;

                // The account popover, opened as the row's click does.
                AccountBar bar = window.GetVisualDescendants().OfType<AccountBar>().Single();
                var toggle = (Button)bar.GetLogicalChildren().Single();
                toggle.Flyout!.ShowAt(toggle);
                Pump(window);
                Shoot(window, "design-account-popover-light");
                toggle.Flyout.Hide();

                shell.OpenAccountCommand.Execute(null);
                Pump(window);
                Shoot(window, "design-account-card-light");
                shell.CloseAccountCommand.Execute(null);

                // A tutorial step that points at the sidebar.
                var tutorial = shell.Tutorial!;
                tutorial.Open();
                tutorial.Index = tutorial.Steps
                    .Select(static (step, index) => (step, index))
                    .First(static pair => pair.step.TargetName == Daynote.App.Onboarding.TutorialTargets.TabTodo)
                    .index;
                Pump(window);
                Shoot(window, "design-tutorial-light");
                tutorial.SkipCommand.Execute(null);

                LocalizationService.Instance.SetLanguage(AppLanguage.English);
                Pump(window);
                Shoot(window, "design-editor-en-light");
                LocalizationService.Instance.SetLanguage(AppLanguage.Korean);

                // The narrowest window the shell allows: the week strip drops to its own line.
                window.Width = 900;
                window.Height = 600;
                Pump(window);
                ShootAt(window, "design-narrow-light", 900, 600);
            });
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(original);
        }
    }

    /// <summary>
    /// The prototype's <c>sampleData()</c>: six notes over nine days, two favourites, three files, the
    /// to-dos with due dates relative to today.
    /// </summary>
    private static void Seed(DesktopShellViewModel shell)
    {
        LocalDate today = shell.SelectedDate;
        string Md(int offset)
        {
            DateOnly day = LocalDates.ToDateOnly(LocalDates.AddDays(today, offset));
            return day.ToString("MM/dd", CultureInfo.InvariantCulture);
        }

        var notes = new (int Offset, string Title, bool Favorite, string[] Tags, string Body)[]
        {
            (-6, "월간 보고 초안", true, ["보고"],
                "7월 월간 보고 핵심 요약\n\n- MAU 12.4만 (+8.2% MoM)\n- 신규 결제 전환율 3.1%\n- 주요 이슈: 검색 응답 지연 → 인덱스 개선으로 해결"),
            (-4, "뉴스클리핑", false, ["뉴스클리핑"],
                "오늘의 업계 뉴스 정리\n\n- 관리단위 식별·연결 ID 플랫폼 사업 공고\n- NFC 기반 모바일 순찰관리 서비스 사례\n- 현장근무자용 모바일 업무 플랫폼 동향"),
            (-1, "고객 미팅 메모", false, ["고객"],
                $"A사 정기 미팅\n\n- 대시보드 로딩 속도 개선 요청\n- 엑셀 내보내기 기능 문의 → 8월 로드맵 안내\n- 계약 갱신은 9월 초 논의 예정\n\n-[] 속도 개선 이슈 티켓 생성 ({Md(1)} 12:00)"),
            (2, "휴가 전 인수인계", false, ["일정"],
                $"8월 첫째 주 휴가 전 정리할 것\n\n-[] 담당 이슈 재배정 ({Md(2)} 18:00)\n-[] 자동응답 설정"),
            (0, "주간회의 준비", true, ["회의", "기획"],
                $"이번 주 주간회의 안건 정리\n\n1. 2분기 지표 리뷰\n2. 신규 기능 우선순위 논의\n3. 채용 진행 상황 공유\n\n-[] 회의자료 초안 공유 ({Md(1)} 10:00)\n-[] 회의실 예약 확인 ({Md(0)} 17:00)\n-[x] 지난주 액션아이템 정리"),
            (0, "배포 체크리스트", false, ["배포"],
                $"v2.4.0 배포 전 확인사항\n\n-[] 스테이징 회귀 테스트 ({Md(2)} 14:00)\n-[] 릴리즈 노트 작성\n-[x] DB 마이그레이션 스크립트 리뷰\n\n※ 배포 후 30분간 에러 모니터링 필수"),
        };

        foreach (var note in notes)
        {
            Wait(shell.SelectDateAsync(LocalDates.AddDays(today, note.Offset)));
            Pump();
            Wait(shell.NewNoteCommand.ExecuteAsync(null));
            Pump();
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

            Pump();
        }

        foreach ((string name, int bytes) in new[] { ("요구사항_정의서_v3.docx", 482_134), ("화면설계_홈.png", 1_204_833) })
        {
            using var content = new MemoryStream(new byte[bytes]);
            Wait(shell.Files.AddFromStreamAsync(name, content));
        }

        // The first of today's notes is the one the design opens on.
        Wait(shell.SelectDayNoteCommand.ExecuteAsync(shell.Notes.Tabs.First(t => !t.IsProjection)));
        Wait(shell.Todo.RefreshAsync());
        Wait(shell.Favorites.RefreshAsync());
        Wait(shell.TagPanel.RefreshAsync());
        Wait(shell.Week.RefreshAsync());
        Wait(shell.Calendar.LoadAsync());
        Pump();
    }

    private static void Show(DesktopShellViewModel shell, Action<DesktopShellViewModel> change)
    {
        change(shell);
        Pump();
    }

    private static void Shoot(Window window, string name) => ShootAt(window, name, Width, Height);

    private static void ShootAt(Window window, string name, int width, int height)
    {
        Pump(window);
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Pump(window);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        using WriteableBitmap? bitmap = window.CaptureRenderedFrame();
        Assert.IsNotNull(bitmap, $"{name}: nothing rendered.");
        Assert.AreEqual(width, bitmap.PixelSize.Width, $"{name}: wrong width.");
        Assert.AreEqual(height, bitmap.PixelSize.Height, $"{name}: wrong height.");

        Directory.CreateDirectory(FramesDirectory);
        bitmap.Save(Path.Combine(FramesDirectory, name + ".png"), new PngBitmapEncoderOptions());
    }

    private static void Wait(Task work)
    {
        for (int i = 0; i < 400 && !work.IsCompleted; i += 1)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }

        Assert.IsTrue(work.IsCompleted, "A step never completed.");
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
