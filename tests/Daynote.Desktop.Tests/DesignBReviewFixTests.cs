using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Daynote.App.Composition;
using Daynote.App.Input;
using Daynote.App.Shell.Product;
using Daynote.Core.Domain;
using Daynote.Core.Notes;
using Daynote.Desktop.Platform;
using Daynote.Desktop.ViewModels;
using Daynote.Desktop.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Desktop.Tests;

/// <summary>Regressions found in review of the design-B port.</summary>
[TestClass]
public sealed class DesignBReviewFixTests
{
    [TestMethod]
    public void Closing_settings_mid_capture_stops_the_capture()
    {
        // Left running, the window's key filter swallows every key for the capture and the next chord
        // silently rebinds the row. Escape always cancelled it; the scrim and the page list did not.
        TestServices.WithInitialisedShell((_, shell) =>
        {
            DesktopSettingsViewModel settings = shell.SettingsViewModel!;
            shell.OpenSettingsCommand.Execute(null);
            settings.InAppShortcuts[0].StartCaptureCommand.Execute(null);
            Assert.IsTrue(settings.IsCapturing, "The scenario needs a capture running.");

            shell.CloseSettingsCommand.Execute(null);
            Assert.IsFalse(settings.IsCapturing, "The capture outlived the dialog.");

            shell.OpenSettingsCommand.Execute(null);
            settings.StartHotkeyCaptureCommand.Execute(null);
            settings.Section = SettingsSection.Data;
            Assert.IsFalse(settings.IsCapturing, "The capture outlived its page.");
        });
    }

    [TestMethod]
    public void Every_design_brush_has_a_high_contrast_stand_in()
    {
        string markup = File.ReadAllText(Path.Combine(DesktopRoot, "Themes", "Daynote.Desk.axaml"));
        string[] keys =
        [
            .. Regex.Matches(markup, @"x:Key=""Daynote\.Desk\.Brush\.([A-Za-z0-9.]+)""")
                .Select(static m => m.Groups[1].Value)
                .Distinct()
                .Order(),
        ];

        Assert.IsGreaterThan(10, keys.Length, "The scan found suspiciously few design brushes.");
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            keys.Except(DeskHighContrastAliases.Map.Keys).ToArray(),
            "These design brushes would keep their colours in a high-contrast session.");

        ResourceDictionary derived = DeskHighContrastAliases.AddTo(DerivedHighContrastBrushes.Build(dark: false));
        foreach (string key in keys)
        {
            Assert.IsTrue(derived.ContainsKey("Daynote.Desk.Brush." + key), $"{key} was not added to the high-contrast dictionary.");
        }
    }

    [TestMethod]
    public void A_hidden_card_takes_no_slot_in_the_grid()
    {
        HeadlessAppFixture.OnUiThread(() =>
        {
            var grid = new AutoFillGrid { MinItemWidth = 100, Gap = 10 };
            var hidden = new Border { Height = 50, IsVisible = false };
            var shown = new Border { Height = 50 };
            grid.Children.Add(hidden);
            grid.Children.Add(shown);

            grid.Measure(new Size(210, double.PositiveInfinity));
            grid.Arrange(new Rect(0, 0, 210, grid.DesiredSize.Height));

            Assert.AreEqual(0, shown.Bounds.X, "The visible card was pushed along by a hidden one.");
            Assert.AreEqual(50, grid.DesiredSize.Height);
        });
    }

    [TestMethod]
    public void The_week_strip_draws_the_last_week_asked_for_even_when_it_answers_first()
    {
        // Two quick steps whose month reads finish out of order: the newer one answers first, the
        // older one after. The strip has to end on the newer week, which is the day on screen.
        var repository = DelayedMonthRepository.Create(out DelayedMonthRepository calls);
        var strip = new WeekStripViewModel(new SystemClock(), repository, static _ => Task.CompletedTask);

        LocalDate older = LocalDates.FromDateOnly(new DateOnly(2026, 3, 11));
        LocalDate newer = LocalDates.FromDateOnly(new DateOnly(2026, 6, 17));
        Task first = strip.ShowAsync(older);
        Task second = strip.ShowAsync(newer);

        calls.Pending[1].SetResult([]);
        second.GetAwaiter().GetResult();
        calls.Pending[0].SetResult([]);
        first.GetAwaiter().GetResult();

        Assert.AreEqual(newer, strip.SelectedDate);
        Assert.AreEqual(WeekStripViewModel.WeekStart(newer), strip.Days[0].Date, "The older week's late answer was drawn over the newer week.");
        Assert.IsTrue(strip.Days.Single(static d => d.IsSelected).Date == newer);
    }

    [TestMethod]
    public void Closing_the_palette_gives_the_keyboard_back()
    {
        TestServices.WithInitialisedShell((window, shell) =>
        {
            shell.NewNoteCommand.Execute(null);
            Pump(window);
            TextBox editor = window.FindControl<TextBox>("Editor")!;
            editor.Focus();
            Assert.IsTrue(editor.IsFocused, "The scenario needs the note body focused.");

            shell.OpenPaletteCommand.Execute(null);
            Pump(window);
            TextBox box = window.FindControl<TextBox>("SearchBox")!;
            Assert.IsTrue(box.IsFocused);

            shell.ClosePaletteCommand.Execute(null);
            Pump(window);

            Assert.IsFalse(box.IsFocused, "The hidden query box kept the keyboard.");
            Assert.IsTrue(editor.IsFocused, "The note body did not get the keyboard back.");
        });
    }

    private static void Pump(Window window)
    {
        for (int i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Thread.Sleep(5);
        }
    }

    /// <summary>
    /// A repository whose month summaries answer only when the test says so; every other member
    /// throws, because the week strip reads nothing else.
    /// </summary>
    public class DelayedMonthRepository : DispatchProxy
    {
        public List<TaskCompletionSource<IReadOnlyList<DateContentSummary>>> Pending { get; } = [];

        public static INoteRepository Create(out DelayedMonthRepository calls)
        {
            INoteRepository proxy = Create<INoteRepository, DelayedMonthRepository>();
            calls = (DelayedMonthRepository)(object)proxy;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(INoteRepository.GetMonthContentSummaryAsync))
            {
                throw new NotSupportedException(targetMethod?.Name);
            }

            // One answer per call: the first week reads one month, so call index = ShowAsync order.
            var pending = new TaskCompletionSource<IReadOnlyList<DateContentSummary>>();
            Pending.Add(pending);
            return new ValueTask<IReadOnlyList<DateContentSummary>>(pending.Task);
        }
    }

    private static string DesktopRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "Daynote.Desktop")))
            {
                directory = directory.Parent;
            }

            return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("No repository root."), "src", "Daynote.Desktop");
        }
    }
}
