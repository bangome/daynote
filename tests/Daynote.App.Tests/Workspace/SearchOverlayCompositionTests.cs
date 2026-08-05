using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Diagnostics;
using Daynote.App.Search;
using Daynote.App.Shell;
using Daynote.App.Showcase;
using Daynote.Core.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Workspace;

[TestClass]
public sealed class SearchOverlayCompositionTests
{
    private static readonly LocalDate Day = WorkspaceTestContext.Date("2026-07-20");

    [STATestMethod]
    public void SearchOverlay_ComposesWithPopulatedResultsAndZeroBindingErrors()
    {
        AssertComposes(vm =>
        {
            vm.Query = "zebra";
            vm.Pending.GetAwaiter().GetResult();
            Assert.AreEqual(SearchLoadState.Populated, vm.LoadState);
            Assert.IsTrue(vm.HasResults);
        });
    }

    [STATestMethod]
    public void SearchOverlay_ComposesEmptyIdleStateWithZeroBindingErrors()
    {
        AssertComposes(vm => Assert.AreEqual(SearchLoadState.Idle, vm.LoadState));
    }

    [STATestMethod]
    public void SearchOverlay_ComposesNoResultsStateWithZeroBindingErrors()
    {
        AssertComposes(vm =>
        {
            vm.Query = "no-such-token-zzz";
            vm.Pending.GetAwaiter().GetResult();
            Assert.AreEqual(SearchLoadState.Empty, vm.LoadState);
        });
    }

    private static void AssertComposes(Action<SearchViewModel> arrange)
    {
        Application application = EnsureApplicationResources();
        WorkspaceTestContext context = WorkspaceTestContext.Create();
        try
        {
            context.StoreNoteAsync(Day, "Zebra note", "zebra body content").GetAwaiter().GetResult();
            using var vm = new SearchViewModel(
                context.SearchService, new RecordingSearchActivation(), new ImmediateSearchScheduler(), TimeSpan.Zero)
            {
                IsOpen = true,
            };
            arrange(vm);

            var listener = new BindingErrorListener();
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning | SourceLevels.Error;
            try
            {
                var overlay = new SearchOverlay { DataContext = vm };
                overlay.Measure(new Size(800, 600));
                overlay.Arrange(new Rect(0, 0, 800, 600));
                overlay.ApplyTemplate();
                overlay.UpdateLayout();

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
