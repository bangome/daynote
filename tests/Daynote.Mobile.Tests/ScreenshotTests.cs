using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Daynote.Mobile.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// Renders each phone screen to a PNG, at handset size, in both themes.
/// </summary>
/// <remarks>
/// These are not assertions about pixels; they exist so a layout can be looked at without an
/// emulator, which is the only way to catch the things a binding test cannot — a tab bar that
/// collides with the home indicator, a day cell too small to hit, text that wraps to three lines.
/// The files land under <c>artifacts/mobile-screens</c> and are not compared to anything.
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
            Application.Current!.RequestedThemeVariant = variantName == "Dark"
                ? Avalonia.Styling.ThemeVariant.Dark
                : Avalonia.Styling.ThemeVariant.Light;

            // A note with real prose, a tag and two checkboxes, so the shots show the app rather
            // than five empty states. The to-do lines are what put rows on the Lists page.
            Seed(shell);

            foreach (MobilePage page in Enum.GetValues<MobilePage>())
            {
                shell.GoToPageCommand.Execute(page);
                Capture(view, $"{page}-{variantName}".ToLowerInvariant());
            }

            // The editor, which is a layer rather than a tab and so is not in the loop above.
            shell.Page = MobilePage.Day;
            shell.IsEditorOpen = true;
            Capture(view, $"editor-{variantName}".ToLowerInvariant());
        });
    }

    /// <summary>Puts one realistic note on today, tagged, with two to-do lines.</summary>
    private static void Seed(MobileShellViewModel shell)
    {
        Pump(() => shell.NewNoteCommand.ExecuteAsync(null));

        shell.Notes.EditorText = string.Join(
            Environment.NewLine,
            "오늘 회의에서 정리한 것",
            string.Empty,
            "-[] 디자인 리뷰 피드백 반영 (9/25 14:00)",
            "-[x] 주간 보고서 초안 보내기",
            string.Empty,
            "다음 주 릴리스는 모바일 베타까지 포함한다.");

        shell.TagInput = "회의";
        Pump(() => shell.CommitTagCommand.ExecuteAsync(null));
        Pump(() => shell.Notes.FlushAsync(Daynote.Core.Notes.FlushReason.NoteChange));
        Pump(() => shell.Todo.RefreshAsync());
        Pump(() => shell.TagPanel.RefreshAsync());

        shell.IsEditorOpen = false;
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
