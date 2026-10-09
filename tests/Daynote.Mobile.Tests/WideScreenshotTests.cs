using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.Core.Domain;
using Daynote.Mobile.Platform;
using Daynote.Mobile.ViewModels;
using Daynote.Mobile.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// The tablet and foldable frames of the two design documents, rendered at their windows' sizes,
/// to be set beside the documents' own frames.
/// </summary>
/// <remarks>
/// Like <see cref="ScreenshotTests"/>, not an assertion about pixels. The frames are named after
/// the design's (T1, F1, ...) and land in <c>artifacts/mobile-screens/wide</c>.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class WideScreenshotTests
{
    private static readonly string OutputDirectory =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "mobile-screens", "wide");

    [TestMethod]
    [DataRow("t1-ipad-landscape-light-ko", 1210, 834, false, "ko", "day", false)]
    [DataRow("t2-ipad-landscape-dark-en", 1210, 834, true, "en", "day", false)]
    [DataRow("t3-ipad-keyboard-card-light-ko", 1210, 834, false, "ko", "capture", true)]
    [DataRow("t4-ipad-note-open-light-ko", 1210, 834, false, "ko", "editor", false)]
    [DataRow("t6-ipad-portrait-light-ko", 834, 1210, false, "ko", "day", false)]
    [DataRow("t6b-ipad-13-portrait-light-ko", 1032, 1376, false, "ko", "day", false)]
    [DataRow("t7-sidebar-over-dark-en", 1032, 1376, true, "en", "sidebar", false)]
    [DataRow("f1-zfold-inner-light-ko", 690, 829, false, "ko", "editor", false)]
    [DataRow("f2-zfold-lists-dark-en", 690, 829, true, "en", "lists", false)]
    [DataRow("f4-zfold-search-dark-ko", 690, 829, true, "ko", "search", false)]
    [DataRow("f5-zfold-settings-light-en", 690, 829, false, "en", "settings", false)]
    [DataRow("f6-zfold-cover-light-ko", 344, 882, false, "ko", "day", false)]
    [DataRow("f9-zflip-dark-ko", 411, 1006, true, "ko", "day", false)]
    public void The_wide_frames_render(string name, double width, double height, bool dark, string language, string screen, bool keyboard)
    {
        Directory.CreateDirectory(OutputDirectory);
        AppLanguage original = LocalizationService.Instance.Language;
        TestServices.WithInitialisedShell(width, height, (view, shell) =>
        {
            try
            {
                LocalizationService.Instance.SetLanguage(language == "ko" ? AppLanguage.Korean : AppLanguage.English);
                shell.IsDark = dark;
                view.PreviewSafeArea = new Thickness(0, 24, 0, 20);
                view.Device = new Keyboard(keyboard);
                LocalDate today = LocalDates.FromDateOnly(DateOnly.FromDateTime(DateTime.Now));
                ScreenshotTests.Seed(shell, today);
                ScreenshotTests.Pump(() => shell.Notes.SelectNoteAsync(shell.Notes.Tabs.First(t => t.Title == "주간회의 준비")));

                switch (screen)
                {
                    case "editor":
                        ScreenshotTests.Pump(() => shell.OpenNoteCommand.ExecuteAsync(shell.Notes.Tabs.First(t => t.Title == "주간회의 준비")));
                        break;
                    case "capture":
                        ScreenshotTests.Pump(() => shell.OpenNoteCommand.ExecuteAsync(shell.Notes.Tabs.First(t => t.Title == "주간회의 준비")));
                        Settle(view);
                        TextBox body = view.FindControl<EditorPage>("Editor")!.FindControl<TextBox>("Body")!;
                        body.Text += "\n회의자료 초안 공유 @오늘 5시";
                        body.CaretIndex = body.Text!.Length;
                        break;
                    case "lists":
                        shell.GoToPageCommand.Execute(MobilePage.Lists);
                        shell.SelectListCommand.Execute(Daynote.App.Shell.Product.RightTab.Favorites);
                        break;
                    case "search":
                        shell.GoToPageCommand.Execute(MobilePage.Search);
                        shell.Search.Query = "회의";
                        ScreenshotTests.Pump(() => shell.Search.SearchNowAsync("회의"));
                        break;
                    case "settings":
                        shell.GoToPageCommand.Execute(MobilePage.Settings);
                        break;
                    case "sidebar":
                        shell.ToggleSidebarCommand.Execute(null);
                        break;
                }

                Settle(view);
                using WriteableBitmap? frame = (TopLevel.GetTopLevel(view) as Window)?.CaptureRenderedFrame();
                frame?.Save(Path.Combine(OutputDirectory, name + ".png"), new PngBitmapEncoderOptions());
            }
            finally
            {
                shell.IsDark = false;
                LocalizationService.Instance.SetLanguage(original);
            }
        });
    }

    private static void Settle(Control view)
    {
        for (int i = 0; i < 5; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            TopLevel.GetTopLevel(view)?.UpdateLayout();
        }
    }

    private sealed class Keyboard(bool attached) : IDeviceShape
    {
        public Rect? Hinge => null;

        public bool HasHardwareKeyboard => attached;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }
}
