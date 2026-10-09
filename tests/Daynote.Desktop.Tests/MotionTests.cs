using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Daynote.App.Composition;
using Daynote.Core.Agenda;
using Daynote.Desktop.Platform;
using Daynote.Desktop.ViewModels;
using Daynote.Desktop.Views;
using Daynote.Motion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Desktop.Tests;

/// <summary>
/// The motion spec on the Mac: the reduced-motion read, the day panel's tick without its pulse
/// (M3) and the arrival of a new row (M2), and the palette's open and close (M6), with the tick
/// rendered frame by frame under <c>artifacts/motion-frames</c>.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class MotionTests
{
    private static readonly string FramesDirectory =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "motion-frames");

    [TestCleanup]
    public void Restore()
    {
        MotionEnvironment.Flavor = MotionFlavor.Desktop;
        MotionEnvironment.Platform = null;
        MotionEnvironment.ReduceMotionOverride = null;
        MotionEnvironment.Instant = true;
        MotionPlayer.Interceptor = null;
    }

    [TestMethod]
    public void The_mac_reduced_motion_setting_reads_without_failing()
    {
        // Whatever this Mac is set to; the point is that the Objective-C call goes through and the
        // environment follows it.
        var platform = new DesktopMotionPlatform();
        MotionEnvironment.Platform = platform;
        Assert.AreEqual(platform.PrefersReducedMotion, MotionEnvironment.ReduceMotion);
        platform.Recheck();
    }

    [TestMethod]
    public void M3_the_day_panel_tick_renders_frame_by_frame_without_a_pulse()
    {
        TestServices.WithInitialisedShell((window, shell) =>
        {
            AddTodo(shell, "회의실 예약 확인");
            TodoCheck(window, out DeskTodoCheck check, out Control row);
            Rect crop = Crop(row, window);

            var captured = new List<Storyboard>();
            Intercept(captured);
            check.Toggle = null;
            Click(window, check);

            Assert.HasCount(1, captured);
            Assert.AreEqual(760, captured[0].Duration.TotalMilliseconds, 1, "The Mac keeps the strikethrough's timing.");
            RenderFrames(window, "m3-mac", captured, 760, 12, crop);
        });
    }

    [TestMethod]
    public void M2_a_new_todo_arrives_in_the_day_panel()
    {
        TestServices.WithInitialisedShell((window, shell) =>
        {
            AddTodo(shell, "회의실 예약 확인");
            Settle(window);

            var played = new List<string>();
            MotionPlayer.Interceptor = (_, channel, board) =>
            {
                played.Add(channel);
                board.Seek(board.Duration);
                return Task.FromResult(true);
            };

            AddTodo(shell, "퇴근 전 로그 확인");
            Settle(window);
            Assert.Contains("arrive", played, "The new row did not arrive.");
            Assert.AreEqual(1, played.Count(static c => c == "arrive"), "Only the new row arrives; the one already there stays put.");
        });
    }

    [TestMethod]
    public void M6_the_palette_grows_in_and_fades_out_before_it_hides()
    {
        TestServices.WithInitialisedShell((window, shell) =>
        {
            Border scrim = window.FindControl<Border>("PaletteScrim")!;
            Border palette = window.FindControl<Border>("Palette")!;
            var captured = new List<Storyboard>();
            Intercept(captured);

            shell.OpenPaletteCommand.Execute(null);
            Assert.IsTrue(scrim.IsVisible);
            Assert.HasCount(1, captured);
            Assert.AreEqual(180, captured[0].Duration.TotalMilliseconds, 1);
            captured[0].Seek(TimeSpan.Zero);
            Assert.AreEqual(0.96, MotionTransform.For(palette).ScaleX, 1e-9, "It starts at 96%.");
            captured[0].Seek(captured[0].Duration);
            Assert.AreEqual(1, MotionTransform.For(palette).ScaleX, 1e-9);

            MotionPlayer.Interceptor = null;
            MotionEnvironment.Instant = true;
            shell.ClosePaletteCommand.Execute(null);
            Settle(window);
            Assert.IsFalse(scrim.IsVisible, "Closed, once its fade has played.");
        });
    }

    /// <summary>
    /// Two ticks inside the 600 ms wait: the first one's write rebuilds the day panel under the
    /// second, which must still be written once, and a quit writes whatever is still held.
    /// </summary>
    [TestMethod]
    public void M3_ticks_held_across_a_rebuild_are_all_written()
    {
        TestServices.WithInitialisedShell((window, shell) =>
        {
            AddTodo(shell, "회의실 예약 확인");
            AddTodo(shell, "퇴근 전 로그 확인");
            AddTodo(shell, "릴리즈 노트 작성");
            Settle(window);

            var gates = new List<TaskCompletionSource>();
            MotionEnvironment.Instant = false;
            shell.Ticks.Delay = (_, token) =>
            {
                var gate = new TaskCompletionSource();
                token.Register(() => gate.TrySetCanceled(token));
                gates.Add(gate);
                return gate.Task;
            };

            Click(window, Check(window, "회의실 예약 확인"));
            Click(window, Check(window, "퇴근 전 로그 확인"));
            Click(window, Check(window, "릴리즈 노트 작성"));
            Assert.AreEqual(3, shell.Ticks.Count);

            gates[0].SetResult();
            Wait(Task.Delay(100));
            Settle(window);
            Assert.AreEqual(2, shell.Ticks.Count);

            gates[1].SetResult();
            Wait(Task.Delay(100));
            Settle(window);

            // Quitting writes the one still held.
            Wait(shell.Ticks.CommitAll());
            Wait(shell.Todo.RefreshAsync());
            Settle(window);
            Assert.IsEmpty(shell.DayTodos, "Every held tick should have been written, once.");
            Assert.HasCount(3, shell.DayTodosDone);
        });
    }

    private static DeskTodoCheck Check(MainWindow window, string title) =>
        window.FindControl<ItemsControl>("DayTodoList")!.GetVisualDescendants().OfType<DeskTodoCheck>()
            .Single(c => (c.DataContext as Daynote.App.Shell.Product.TodoItemViewModel)?.Text == title);

    private static void Intercept(List<Storyboard> captured)
    {
        MotionEnvironment.Instant = false;
        MotionPlayer.Interceptor = (_, _, board) =>
        {
            captured.Add(board);
            return new TaskCompletionSource<bool>().Task;
        };
    }

    private static void TodoCheck(MainWindow window, out DeskTodoCheck check, out Control row)
    {
        Settle(window);
        ItemsControl list = window.FindControl<ItemsControl>("DayTodoList")!;
        check = list.GetVisualDescendants().OfType<DeskTodoCheck>().First(static c => !c.IsChecked);
        row = check.GetVisualAncestors().OfType<Control>().First(static c => c.Classes.Contains("todorow"));
    }

    private static void Click(Window window, Control target)
    {
        Point centre = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, Avalonia.Input.MouseButton.Left);
        window.MouseUp(centre, Avalonia.Input.MouseButton.Left);
        window.MouseMove(new Point(1, 1));
    }

    private static void AddTodo(DesktopShellViewModel shell, string title)
    {
        var agenda = TestServices.CurrentProvider!.GetRequiredService<IAgendaRepository>();
        DateOnly day = LocalDates.ToDateOnly(shell.SelectedDate);
        Wait(agenda.SaveAsync(new AgendaItem(
            Guid.NewGuid(),
            AgendaList.DefaultId,
            AgendaKind.Task,
            title,
            string.Empty,
            "Asia/Seoul",
            StartsAt: null,
            EndsAt: null,
            DueAt: new WallClock(day.ToDateTime(new TimeOnly(14, 0))),
            HasDueTime: true,
            Rrule: null,
            SeriesId: null,
            RecurrenceId: null,
            AgendaStatus.NeedsAction,
            CompletedUtc: null,
            Priority: 0,
            TimelineVisibility.Auto,
            SourceNoteId: null,
            ExceptionDates: [],
            AgendaAlert.Default,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow)).AsTask());

        // The day panel rebuilds from the to-do panel's refresh.
        Wait(shell.Todo.RefreshAsync());
    }

    private static void RenderFrames(Window window, string name, IReadOnlyList<Storyboard> boards, double totalMs, int count, Rect crop)
    {
        string directory = Path.Combine(FramesDirectory, name);
        Directory.CreateDirectory(directory);
        var frames = new List<WriteableBitmap>();
        for (int i = 0; i < count; i++)
        {
            var at = TimeSpan.FromMilliseconds(totalMs * i / (count - 1));
            foreach (Storyboard board in boards)
            {
                board.Seek(at);
            }

            Settle(window);
            using WriteableBitmap frame = window.CaptureRenderedFrame()!;
            WriteableBitmap part = Cut(frame, crop);
            part.Save(Path.Combine(directory, $"{name}-{i:00}-{at.TotalMilliseconds:000}ms.png"), new PngBitmapEncoderOptions());
            frames.Add(part);
        }

        using WriteableBitmap strip = Stack(frames);
        strip.Save(Path.Combine(FramesDirectory, $"{name}-strip.png"), new PngBitmapEncoderOptions());
        foreach (WriteableBitmap frame in frames)
        {
            frame.Dispose();
        }
    }

    private static Rect Crop(Control control, Control root)
    {
        Point at = control.TranslatePoint(default, root)!.Value;
        return new Rect(Math.Max(0, at.X - 4), Math.Max(0, at.Y - 4), control.Bounds.Width + 8, control.Bounds.Height + 8);
    }

    private static WriteableBitmap Cut(WriteableBitmap frame, Rect rect)
    {
        var size = new PixelSize((int)rect.Width, (int)rect.Height);
        var part = new WriteableBitmap(size, new Vector(96, 96), frame.Format ?? PixelFormat.Rgba8888, AlphaFormat.Premul);
        using ILockedFramebuffer from = frame.Lock();
        using ILockedFramebuffer to = part.Lock();
        var row = new byte[Math.Min(size.Width, from.Size.Width - (int)rect.X) * 4];
        for (int y = 0; y < size.Height && (int)rect.Y + y < from.Size.Height; y++)
        {
            Marshal.Copy(from.Address + (((int)rect.Y + y) * from.RowBytes) + ((int)rect.X * 4), row, 0, row.Length);
            Marshal.Copy(row, 0, to.Address + (y * to.RowBytes), row.Length);
        }

        return part;
    }

    private static WriteableBitmap Stack(IReadOnlyList<WriteableBitmap> frames)
    {
        int width = frames[0].PixelSize.Width;
        int height = frames[0].PixelSize.Height;
        var strip = new WriteableBitmap(
            new PixelSize(width, (height + 2) * frames.Count), new Vector(96, 96), frames[0].Format ?? PixelFormat.Rgba8888, AlphaFormat.Premul);
        using ILockedFramebuffer to = strip.Lock();
        var row = new byte[width * 4];
        for (int i = 0; i < frames.Count; i++)
        {
            using ILockedFramebuffer from = frames[i].Lock();
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(from.Address + (y * from.RowBytes), row, 0, row.Length);
                Marshal.Copy(row, 0, to.Address + (((i * (height + 2)) + y) * to.RowBytes), row.Length);
            }
        }

        return strip;
    }

    private static void Wait(Task work)
    {
        for (int i = 0; i < 400 && !work.IsCompleted; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        Assert.IsTrue(work.IsCompleted, "A step never completed.");
        work.GetAwaiter().GetResult();
    }

    private static void Settle(Window window)
    {
        for (int i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }
}
