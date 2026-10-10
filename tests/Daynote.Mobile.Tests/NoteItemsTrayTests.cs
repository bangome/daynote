using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Daynote.Core.Agenda;
using Daynote.Mobile.ViewModels;
using Daynote.Mobile.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// Creation feedback (phone §02): the toolbar's "이 노트의 항목 N", the row that rises after 추가,
/// and the sheet behind the count.
/// </summary>
[TestClass]
public sealed class NoteItemsTrayTests
{
    [TestMethod]
    public void What_the_note_made_is_counted_in_the_toolbar()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            OpenEditor(view, shell);
            Assert.IsFalse(shell.HasNoteItems, "A new note has made nothing, so there is no count to show.");

            Capture(shell, "회의실 예약 확인", 0);
            Capture(shell, "회의자료 초안 공유", 1);

            Assert.AreEqual(2, shell.NoteItems.Count);
            Assert.Contains("2", shell.NoteItemCountText);
            Assert.IsTrue(shell.HasNoteItems);
        });
    }

    [TestMethod]
    public void The_collection_is_the_notes_own_whatever_date_things_landed_on()
    {
        // The surprising half of what this list is: a note written on Wednesday holds the item it
        // made for Friday, and that item is nowhere else on this screen.
        TestServices.WithInitialisedShell((view, shell) =>
        {
            OpenEditor(view, shell);
            Capture(shell, "업체에 전화", 1);

            Assert.AreEqual("업체에 전화", shell.NoteItems.Single().Text);

            // Another note's items are not this one's.
            Pump(() => shell.CloseEditorAsync());
            Pump(() => shell.NewNoteCommand.ExecuteAsync(null));

            Assert.IsEmpty(shell.NoteItems);
            Assert.IsFalse(shell.HasNoteItems);
        });
    }

    [TestMethod]
    public void The_made_row_rises_in_place_and_settles_into_the_count()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            var waits = new List<TimeSpan>();
            TaskCompletionSource released = Hold(shell, waits);
            OpenEditor(view, shell);

            Make(shell, "퇴근 전 로그 확인", 0);
            PumpUntil(() => shell.JustMade is not null, "Nothing rose to say the item had been made.");

            Assert.IsNotNull(shell.JustMade);
            Assert.AreEqual("퇴근 전 로그 확인", shell.JustMade!.Text);
            Assert.AreEqual(MobileStrings.Get("MobileJustNow"), shell.JustMadeWhenText);
            Assert.IsFalse(shell.IsJustMadeElsewhere, "It went to the day being written; there is nowhere to go.");
            Assert.AreSequenceEqual([MobileShellViewModel.JustMadeFlash], waits);

            released.SetResult();
            PumpUntil(() => shell.JustMade is null, "The row should have settled back into the count.");

            Assert.Contains("1", shell.NoteItemCountText);
        });
    }

    [TestMethod]
    public void A_row_for_another_day_says_where_it_went_and_offers_to_go()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            TaskCompletionSource released = Hold(shell, []);
            OpenEditor(view, shell);
            Daynote.Core.Domain.LocalDate written = shell.SelectedDate;

            Make(shell, "회의자료 초안 공유", 1);
            PumpUntil(() => shell.JustMade is not null, "Nothing rose to say the item had been made.");

            Assert.IsTrue(shell.IsJustMadeElsewhere);
            Assert.AreNotEqual(MobileStrings.Get("MobileJustNow"), shell.JustMadeWhenText);

            Pump(() => shell.ViewJustMadeCommand.ExecuteAsync(null));
            released.SetResult();

            Assert.AreEqual(
                Daynote.App.Composition.LocalDates.AddDays(written, 1),
                shell.SelectedDate,
                "보기 should land on the day the item went to.");
            Assert.IsFalse(shell.IsEditorOpen, "The day is what 보기 was offering to show.");
        });
    }

    [TestMethod]
    public void A_newer_row_is_not_cleared_by_an_older_ones_wait()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            var gates = new List<TaskCompletionSource>();
            shell.FlashDelay = _ =>
            {
                var gate = new TaskCompletionSource();
                gates.Add(gate);
                return gate.Task;
            };

            OpenEditor(view, shell);
            Make(shell, "첫 번째", 0);
            PumpUntil(() => shell.JustMade is not null, "The first row never rose.");
            Make(shell, "두 번째", 0);
            PumpUntil(() => shell.JustMade?.Text == "두 번째", "The second row never replaced the first.");

            gates[0].SetResult();
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();

            Assert.IsNotNull(shell.JustMade, "The first row's wait took the second row down with it.");
        });
    }

    [TestMethod]
    public void The_sheet_opens_from_the_count_and_closes_on_back()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            OpenEditor(view, shell);

            shell.OpenNoteItemsCommand.Execute(null);
            Assert.IsFalse(shell.IsNoteItemsSheetOpen, "There is nothing to list yet.");

            Capture(shell, "회의실 예약 확인", 0);
            shell.OpenNoteItemsCommand.Execute(null);

            Assert.IsTrue(shell.IsNoteItemsSheetOpen);
            Assert.IsFalse(shell.ShowDock, "A sheet covers the tab bar.");
            Assert.IsTrue(Run(shell.GoBackAsync()));
            Assert.IsFalse(shell.IsNoteItemsSheetOpen);
        });
    }

    /// <summary>Holds every flash open, so a risen row stays up for the assertions.</summary>
    private static TaskCompletionSource Hold(MobileShellViewModel shell, List<TimeSpan> waits)
    {
        var gate = new TaskCompletionSource();
        shell.FlashDelay = wait =>
        {
            waits.Add(wait);
            return gate.Task;
        };
        return gate;
    }

    private static void Capture(MobileShellViewModel shell, string title, int daysAhead)
    {
        // These tests are about the count, not the flash; waiting it out would cost 2.5s a line.
        shell.FlashDelay = _ => Task.CompletedTask;
        int before = shell.NoteItems.Count;
        Make(shell, title, daysAhead);
        PumpUntil(() => shell.NoteItems.Count == before + 1, "추가 did not reach the note's collection.");
    }

    /// <summary>Fills the to-do sheet with a title on a day this many after the note's, and taps 추가.</summary>
    private static void Make(MobileShellViewModel shell, string title, int daysAhead)
    {
        Pump(() => shell.OpenTodoSheetCommand.ExecuteAsync(null));
        shell.Entry.Title = title;
        shell.Entry.Date = Daynote.App.Composition.LocalDates.ToDateOnly(shell.SelectedDate).AddDays(daysAhead);
        shell.CommitTodoSheetCommand.Execute(null);
    }

    private static void OpenEditor(MainView view, MobileShellViewModel shell)
    {
        Pump(() => shell.NewNoteCommand.ExecuteAsync(null));
        Assert.IsNotNull(view.GetVisualDescendants().OfType<EditorPage>().SingleOrDefault(), "No editor on screen.");
    }

    private static bool Run(Task<bool> task)
    {
        bool result = false;
        Pump(async () => result = await task.ConfigureAwait(true));
        return result;
    }

    /// <summary>
    /// Pumps until something becomes true. A command that writes to the database resumes on this
    /// dispatcher more than once, so one <c>RunJobs</c> is not the end of it.
    /// </summary>
    private static void PumpUntil(Func<bool> settled, string because)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!settled())
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, because);
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }

    private static void Pump(Func<Task> work)
    {
        Task task = work();
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted)
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, "The command did not complete within 20 seconds.");
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        task.GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
    }
}
