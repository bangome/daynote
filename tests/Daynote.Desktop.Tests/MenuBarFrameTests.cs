using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Daynote.App.Localization;
using Daynote.Desktop.Platform;
using Daynote.Desktop.ViewModels;
using Daynote.Desktop.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Desktop.Tests;

/// <summary>
/// The menu bar popover drawn to real pixels in the states the design draws (B1–B7), light and
/// dark, Korean and English, so the frames can be set beside the design's own render.
/// </summary>
/// <remarks>
/// Like <see cref="RenderedFrameTests"/>, the assertions are coarse — a picture of the right width,
/// not a blank sheet, and the variants differ — and the PNGs under <c>frames/</c> are the evidence.
/// </remarks>
[TestClass]
public sealed class MenuBarFrameTests
{
    private static readonly string FramesDirectory = Path.Combine(AppContext.BaseDirectory, "frames");

    [TestMethod]
    public void B1_default_light_korean() =>
        Render("menubar-b1-light-ko", ThemeVariant.Light, AppLanguage.Korean, _ => { });

    [TestMethod]
    public void B2_todo_typed_light_korean() =>
        Render("menubar-b2-light-ko", ThemeVariant.Light, AppLanguage.Korean, model =>
            MenuBarTests.Type(model, "회의자료 초안 공유"));

    [TestMethod]
    public void B3_just_made_dark_korean() =>
        Render("menubar-b3-dark-ko", ThemeVariant.Dark, AppLanguage.Korean, model =>
        {
            MenuBarTests.Type(model, "회의자료 초안 공유");
            MenuBarTests.Wait(model.SubmitAsync());
        });

    [TestMethod]
    public void B4_todo_typed_dark_english() =>
        Render("menubar-b4-dark-en", ThemeVariant.Dark, AppLanguage.English, model =>
            MenuBarTests.Type(model, "Send the report"));

    [TestMethod]
    public void B5_note_line_light_english() =>
        Render("menubar-b5-light-en", ThemeVariant.Light, AppLanguage.English, model =>
        {
            MenuBarTests.Wait(model.SelectNoteModeCommand.ExecuteAsync(null));
            MenuBarTests.Type(model, "Idea: shorten onboarding to 3 steps");
        });

    [TestMethod]
    public void B6_note_line_dark_korean() =>
        Render("menubar-b6-dark-ko", ThemeVariant.Dark, AppLanguage.Korean, model =>
        {
            MenuBarTests.Wait(model.SelectNoteModeCommand.ExecuteAsync(null));
            MenuBarTests.Type(model, "아이디어: 온보딩 3단계로");
        });

    [TestMethod]
    public void B8_note_appended_light_korean() =>
        Render("menubar-b8-light-ko", ThemeVariant.Light, AppLanguage.Korean, model =>
        {
            MenuBarTests.Wait(model.SelectNoteModeCommand.ExecuteAsync(null));
            MenuBarTests.Type(model, "아이디어: 온보딩 3단계로");
            MenuBarTests.Wait(model.SubmitAsync());
        });

    [TestMethod]
    public void B9_default_dark_english() =>
        Render("menubar-b9-dark-en", ThemeVariant.Dark, AppLanguage.English, _ => { });

    [TestMethod]
    public void B7_just_ticked_light_english() =>
        Render("menubar-b7-light-en", ThemeVariant.Light, AppLanguage.English, model =>
            MenuBarTests.Wait(model.Todos[0].ToggleCommand.ExecuteAsync(null)));

    [TestMethod]
    public void The_status_symbols_draw()
    {
        HeadlessAppFixture.OnUiThread(() =>
        {
            byte[] template = StatusSymbol.TemplatePng();
            Directory.CreateDirectory(FramesDirectory);
            File.WriteAllBytes(Path.Combine(FramesDirectory, "menubar-status-template.png"), template);
            using (var bitmap = new Bitmap(new MemoryStream(template)))
            {
                Assert.AreEqual(36, bitmap.PixelSize.Width);
            }

            using var stream = new FileStream(Path.Combine(FramesDirectory, "menubar-tray-icon-4.png"), FileMode.Create);
            StatusSymbol.TrayIcon(4).Save(stream);
        });
    }

    private static void Render(string name, ThemeVariant variant, AppLanguage language, Action<MenuBarViewModel> arrange)
    {
        MenuBarTests.WithMenuBar(
            (model, _) =>
            {
                model.ChordText = "⌥⌘Space";
                arrange(model);

                var window = new MenuBarPopover { DataContext = model, RequestedThemeVariant = variant };
                try
                {
                    window.PlayEntrance(PopoverEntrance.None, reduceMotion: false);
                    window.Show();
                    for (int i = 0; i < 10; i++)
                    {
                        Dispatcher.UIThread.RunJobs();
                        Thread.Sleep(5);
                    }

                    window.UpdateLayout();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using WriteableBitmap? frame = window.CaptureRenderedFrame();
                    Assert.IsNotNull(frame, $"{name}: nothing was rendered.");

                    Directory.CreateDirectory(FramesDirectory);
                    frame.Save(Path.Combine(FramesDirectory, name + ".png"), new PngBitmapEncoderOptions());

                    // The card plus its shadow margin, and a card that is more than its frame.
                    Assert.AreEqual(340 + 48, frame.PixelSize.Width, $"{name}: the card is not the design's 340 wide.");
                    Assert.IsGreaterThan(300, frame.PixelSize.Height, $"{name}: the popover is too short to hold its sections.");
                }
                finally
                {
                    window.Close();
                }
            },
            language: language);
    }
}
