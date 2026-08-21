using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Diagnostics;
using Daynote.App.Shell;
using Daynote.App.Showcase;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Workspace;

[TestClass]
public sealed class ShellCompositionTests
{
    [STATestMethod]
    public void Shell_ComposesWideWithZeroBindingErrors()
    {
        AssertComposes(1300, main => Assert.IsTrue(main.IsWide));
    }

    [STATestMethod]
    public void Shell_ComposesRegularWithZeroBindingErrors()
    {
        AssertComposes(900, main => Assert.IsTrue(main.IsRegular));
    }

    [STATestMethod]
    public void Shell_ComposesCompactWorkspace()
    {
        AssertComposes(760, main => Assert.IsTrue(main.IsCompact));
    }

    private static void AssertComposes(double width, Action<MainWindowViewModel> verify)
    {
        Application application = EnsureApplicationResources();
        WorkspaceTestContext context = WorkspaceTestContext.Create();
        try
        {
            context.Main.InitializeAsync().GetAwaiter().GetResult();
            context.Main.UpdateEffectiveWidth(width);

            verify(context.Main);

            var listener = new BindingErrorListener();
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning | SourceLevels.Error;
            try
            {
                var window = new MainWindow(context.Main);
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(width, 800));
                content.Arrange(new Rect(0, 0, width, 800));
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
            context.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
        application.Resources["Daynote.Convert.InverseBoolToVisibility"] = new InverseBoolToVisibilityConverter();
        application.Resources["Daynote.Convert.EqualsToVisibility"] = new EqualsToVisibilityConverter();
        application.Resources["Daynote.Convert.EqualsToBool"] = new EqualsToBooleanConverter();
        application.Resources["Daynote.Convert.NullToVisibility"] = new NullToVisibilityConverter();
        application.Resources["Daynote.Convert.NullToCollapsed"] = new NullToVisibilityConverter { Invert = true };
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
