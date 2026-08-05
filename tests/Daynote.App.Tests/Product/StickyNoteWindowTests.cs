using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Daynote.App.Localization;
using Daynote.App.Shell;
using Daynote.App.Shell.Product;
using Daynote.App.Showcase;
using Daynote.App.Tests.Workspace;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Product;

[TestClass]
public sealed class StickyNoteWindowTests
{
    [STATestMethod]
    public void MirrorsLiveEditorBothWaysAndSupportsPinResizeAndClose()
    {
        Application application = EnsureApplicationResources();

        // Drive the async setup synchronously so every window touch stays on this STA thread
        // (an await here would resume the continuation on a non-STA pool thread and WPF would throw).
        WorkspaceTestContext context = WorkspaceTestContext.Create();
        WorkspaceTestContext.ProductShellHarness? harness = null;
        try
        {
            context.StoreNoteAsync(WorkspaceTestContext.Date("2026-07-20"), "포스트잇 QA 제목", "첫 번째 줄\n두 번째 줄")
                .GetAwaiter().GetResult();
            harness = context.BuildProductShell();
            harness.Shell.Notes.LoadAsync(WorkspaceTestContext.Date("2026-07-20")).GetAwaiter().GetResult();

            var window = new StickyNoteWindow(harness.Shell);
            window.Show();
            window.UpdateLayout();

            Assert.IsTrue(window.Topmost);
            Assert.AreEqual(ResizeMode.CanResize, window.ResizeMode);
            Assert.AreEqual(new Thickness(6), WindowChrome.GetWindowChrome(window).ResizeBorderThickness);

            // Opening state mirrors the currently selected note.
            var titleText = (TextBlock)window.FindName("TitleText");
            var bodyText = (TextBox)window.FindName("BodyText");
            Assert.AreEqual("포스트잇 QA 제목", titleText.Text);
            Assert.AreEqual("첫 번째 줄\n두 번째 줄", bodyText.Text);

            // Editor -> post-it: a live body change in the shell shows up in the note.
            harness.Shell.Notes.EditorText = "세 번째 줄";
            window.UpdateLayout();
            Assert.AreEqual("세 번째 줄", bodyText.Text);

            // Post-it -> editor: typing in the note writes straight back to the shared buffer.
            bodyText.Text = "포스트잇에서 직접 편집";
            Assert.AreEqual("포스트잇에서 직접 편집", harness.Shell.Notes.EditorText);

            // Title stays in step with the selected note too.
            harness.Shell.Notes.SelectedTab!.Title = "새 제목";
            window.UpdateLayout();
            Assert.AreEqual("새 제목", titleText.Text);

            Capture(window);

            var pinButton = (Button)window.FindName("PinButton");
            pinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Assert.IsFalse(window.Topmost);
            Assert.AreEqual(AppStrings.PinStickyNote, AutomationProperties.GetName(pinButton));

            window.Width = 380;
            window.Height = 300;
            Assert.AreEqual(380d, window.Width);
            Assert.AreEqual(300d, window.Height);

            ((Button)window.FindName("CloseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsFalse(window.IsVisible);
        }
        finally
        {
            if (harness is not null)
            {
                harness.DisposeAsync().GetAwaiter().GetResult();
            }

            context.DisposeAsync().GetAwaiter().GetResult();
            application.Resources.MergedDictionaries.Clear();
        }
    }

    private static Application EnsureApplicationResources()
    {
        Application application = Application.Current ?? new Application();
        application.Resources.MergedDictionaries.Clear();
        ShowcaseResources.Load(application, highContrast: false);
        application.Resources["Daynote.Convert.BoolToVisibility"] = new BooleanToVisibilityConverter();
        application.Resources["Daynote.Convert.InverseBool"] = new InverseBooleanConverter();
        application.Resources["Daynote.Convert.EqualsToVisibility"] = new EqualsToVisibilityConverter();
        application.Resources["Daynote.Convert.EqualsToBool"] = new EqualsToBooleanConverter();
        application.Resources["Daynote.Convert.NullToVisibility"] = new NullToVisibilityConverter();
        application.Resources["Daynote.Convert.NullToCollapsed"] = new NullToVisibilityConverter { Invert = true };
        application.Resources["Daynote.Convert.InverseBoolToVisibility"] = new InverseBoolToVisibilityConverter();
        new WpfProductThemeApplier(application).Apply(dark: false);
        return application;
    }

    private static void Capture(StickyNoteWindow window)
    {
        string? evidenceDirectory = Environment.GetEnvironmentVariable("DAYNOTE_STICKY_QA_EVIDENCE");
        if (string.IsNullOrWhiteSpace(evidenceDirectory))
        {
            return;
        }

        Directory.CreateDirectory(evidenceDirectory);
        var target = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(window.ActualWidth)),
            Math.Max(1, (int)Math.Ceiling(window.ActualHeight)),
            96,
            96,
            PixelFormats.Pbgra32);
        target.Render(window);

        using FileStream stream = File.Create(Path.Combine(evidenceDirectory, "sticky-note-window.png"));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        encoder.Save(stream);
    }
}
