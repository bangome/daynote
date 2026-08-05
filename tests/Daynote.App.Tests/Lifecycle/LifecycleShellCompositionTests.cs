using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Diagnostics;
using Daynote.App.Lifecycle;
using Daynote.App.Settings;
using Daynote.App.Shell;
using Daynote.App.Showcase;
using Daynote.App.Tests.Workspace;
using Daynote.Core.Startup;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Lifecycle;

[TestClass]
public sealed class LifecycleShellCompositionTests
{
    [STATestMethod]
    public void Shell_ComposesWithSettings_ZeroBindingErrors()
    {
        Application application = EnsureApplicationResources();
        WorkspaceTestContext context = WorkspaceTestContext.Create();
        try
        {
            context.Main.InitializeAsync().GetAwaiter().GetResult();
            context.Main.UpdateEffectiveWidth(1300);

            context.Main.Settings = new SettingsViewModel(
                new FakeStartupTaskService(StartupTaskState.DisabledByUser),
                new RecordingHotkeyService(), new InMemorySettingsStore(),
                new FakeBackupService(), new FakeBackupFilePicker(),
                new Daynote.App.Input.ConfigurableShortcuts(new InMemorySettingsStore()),
                () => Task.FromResult(true), () => { }, () => { },
                @"C:\Users\Test\AppData\Local\Daynote");
            context.Main.IsSettingsOpen = true;

            var listener = new BindingErrorListener();
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning | SourceLevels.Error;
            try
            {
                var window = new MainWindow(context.Main);
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(1300, 800));
                content.Arrange(new Rect(0, 0, 1300, 800));
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
