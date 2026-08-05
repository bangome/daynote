using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Diagnostics;
using Daynote.App.Shell;
using Daynote.App.Shell.Product;
using Daynote.App.Showcase;
using Daynote.App.Tests.Workspace;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Product;

/// <summary>
/// STA composition tests for the Calendar Notes product window: it measures/arranges with zero data-binding
/// errors in light and dark themes, with panels expanded and collapsed, on each right-panel tab, and with
/// the search dropdown open. Mirrors the legacy shell-composition harness (BindingErrorTraceListener).
/// </summary>
[TestClass]
public sealed class ProductWindowCompositionTests
{
    [STATestMethod]
    public void Composes_WideLight_NoBindingErrors() =>
        AssertComposes(dark: false, configure: null);

    [STATestMethod]
    public void Composes_WideDark_NoBindingErrors() =>
        AssertComposes(dark: true, configure: shell => shell.IsDark = true);

    [STATestMethod]
    public void Composes_CollapsedPanels_NoBindingErrors() =>
        AssertComposes(dark: false, configure: shell =>
        {
            shell.LeftCollapsed = true;
            shell.RightCollapsed = true;
        });

    [STATestMethod]
    public void Composes_FilesTab_NoBindingErrors() =>
        AssertComposes(dark: false, configure: shell => shell.ActiveTab = RightTab.Files);

    [STATestMethod]
    public void Composes_SearchDropdownOpen_NoBindingErrors() =>
        AssertComposes(dark: false, configure: shell => shell.Search.Query = "회의");

    [STATestMethod]
    public void UserPanelToggle_ResizesWindowSoEditorStaysFixed()
    {
        _ = EnsureApplicationResources(dark: false);
        WorkspaceTestContext context = WorkspaceTestContext.Create();
        WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        try
        {
            harness.Shell.InitializeAsync().GetAwaiter().GetResult();
            var window = new ProductWindow(harness.Shell) { Left = 100, Width = 1200 };
            double rightEdge = window.Left + window.Width;

            harness.Shell.ToggleLeftCommand.Execute(null);
            Assert.AreEqual(1200 - (290 + 10), window.Width, "Collapsing the left panel sheds its full width plus gap.");
            Assert.AreEqual(rightEdge, window.Left + window.Width, "Left-panel toggle keeps the window right edge (editor) fixed.");
            Assert.AreEqual(100 + (290 + 10), window.Left, "Collapsing the left panel moves the window's left edge in.");

            harness.Shell.ToggleLeftCommand.Execute(null);
            Assert.AreEqual(1200, window.Width, "Expanding restores the shed width.");
            Assert.AreEqual(100, window.Left, "Expanding the left panel restores the window's left edge.");
            Assert.AreEqual(rightEdge, window.Left + window.Width, "The editor's screen position is unchanged across left toggles.");

            harness.Shell.ToggleRightCommand.Execute(null);
            Assert.AreEqual(1200 - (300 + 10), window.Width, "Collapsing the right panel sheds its full width plus gap.");
            Assert.AreEqual(100, window.Left, "Right-panel toggle leaves the window left edge (editor) fixed.");

            harness.Shell.RightCollapsed = false;
            Assert.AreEqual(1200 - (300 + 10), window.Width, "Non-user (auto) collapse state changes never resize the window.");
        }
        finally
        {
            harness.DisposeAsync().AsTask().GetAwaiter().GetResult();
            context.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static void AssertComposes(bool dark, Action<ProductShellViewModel>? configure)
    {
        Application application = EnsureApplicationResources(dark);
        WorkspaceTestContext context = WorkspaceTestContext.Create();
        WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        try
        {
            harness.Shell.InitializeAsync().GetAwaiter().GetResult();
            configure?.Invoke(harness.Shell);

            var listener = new BindingErrorListener();
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning | SourceLevels.Error;
            try
            {
                var window = new ProductWindow(harness.Shell);
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(1200, 800));
                content.Arrange(new Rect(0, 0, 1200, 800));
                ApplyTemplates(content);
                content.UpdateLayout();

                Assert.IsGreaterThan(0, content.DesiredSize.Height);
                CollectionAssert.AreEqual(
                    Array.Empty<string>(),
                    listener.Errors.ToArray(),
                    $"Binding errors:{Environment.NewLine}{string.Join(Environment.NewLine, listener.Errors)}");
            }
            finally
            {
                PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
            }
        }
        finally
        {
            harness.DisposeAsync().AsTask().GetAwaiter().GetResult();
            context.DisposeAsync().AsTask().GetAwaiter().GetResult();
            application.Resources.MergedDictionaries.Clear();
        }
    }

    private static Application EnsureApplicationResources(bool dark)
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
        application.Resources["Daynote.Convert.LanguageLogo"] = new Daynote.App.Localization.LanguageLogoConverter();

        // Merge the product theme dictionaries via the production applier so DynamicResource brushes resolve.
        new WpfProductThemeApplier(application).Apply(dark);
        return application;
    }

    private static void ApplyTemplates(DependencyObject root)
    {
        if (root is Control control)
        {
            control.ApplyTemplate();
        }

        for (int index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            ApplyTemplates(System.Windows.Media.VisualTreeHelper.GetChild(root, index));
        }
    }

    private sealed class BindingErrorListener : TraceListener
    {
        public List<string> Errors { get; } = [];

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                Errors.Add(message);
            }
        }
    }
}
