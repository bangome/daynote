using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Daynote.Core.Agenda;
using Daynote.Mobile.ViewModels;
using Daynote.Mobile.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// Creation feedback (phone §02): the toolbar's "이 노트의 항목 N", the row that rises after 만들기,
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
            TextBox body = OpenEditor(view, shell);
            Assert.IsFalse(shell.HasNoteItems, "A new note has made nothing, so there is no count to show.");

            Capture(shell, body, "회의실 예약 확인 @오늘");
            Capture(shell, body, "회의자료 초안 공유 @내일");

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
            TextBox body = OpenEditor(view, shell);
            Capture(shell, body, "업체에 전화 @내일");

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
            TextBox body = OpenEditor(view, shell);

            Type(body, "퇴근 전 로그 확인 @오늘");
            shell.CommitCaptureCommand.Execute(null);
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
            TextBox body = OpenEditor(view, shell);
            Daynote.Core.Domain.LocalDate written = shell.SelectedDate;

            Type(body, "회의자료 초안 공유 @내일");
            shell.CommitCaptureCommand.Execute(null);
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

            TextBox body = OpenEditor(view, shell);
            Type(body, "첫 번째 @오늘");
            shell.CommitCaptureCommand.Execute(null);
            PumpUntil(() => shell.JustMade is not null, "The first row never rose.");
            Type(body, "두 번째 @오늘");
            shell.CommitCaptureCommand.Execute(null);
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
            TextBox body = OpenEditor(view, shell);

            shell.OpenNoteItemsCommand.Execute(null);
            Assert.IsFalse(shell.IsNoteItemsSheetOpen, "There is nothing to list yet.");

            Capture(shell, body, "회의실 예약 확인 @오늘");
            shell.OpenNoteItemsCommand.Execute(null);

            Assert.IsTrue(shell.IsNoteItemsSheetOpen);
            Assert.IsFalse(shell.ShowDock, "A sheet covers the tab bar.");
            Assert.IsTrue(Run(shell.GoBackAsync()));
            Assert.IsFalse(shell.IsNoteItemsSheetOpen);
        });
    }

    [TestMethod]
    public void The_at_button_types_an_at_where_it_can_open_the_bar()
    {
        // @ only triggers at the start of a word, which is what keeps an email address out of it.
        // A button that pasted one mid-word would do nothing and look broken.
        TestServices.WithInitialisedShell((view, shell) =>
        {
            TextBox body = OpenEditor(view, shell);
            Type(body, "분기 회고 잡기");

            Button at = view.GetVisualDescendants().OfType<EditorPage>().Single()
                .GetVisualDescendants().OfType<Button>()
                .First(button => button.Classes.Contains("attool"));
            at.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual("분기 회고 잡기 @", body.Text);
            Assert.IsTrue(shell.Capture.IsOpen);
            Assert.IsTrue(shell.Capture.IsPrompting);
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

    private static void Capture(MobileShellViewModel shell, TextBox body, string line)
    {
        // These tests are about the count, not the flash; waiting it out would cost 2.5s a line.
        shell.FlashDelay = _ => Task.CompletedTask;
        Type(body, line);
        Pump(() => shell.CommitCaptureCommand.ExecuteAsync(null));
        Type(body, string.Empty);
    }

    private static TextBox OpenEditor(MainView view, MobileShellViewModel shell)
    {
        Pump(() => shell.NewNoteCommand.ExecuteAsync(null));
        return view.GetVisualDescendants().OfType<EditorPage>().Single()
            .GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "Body");
    }

    private static void Type(TextBox body, string text)
    {
        body.Text = text;
        body.CaretIndex = text.Length;
        Dispatcher.UIThread.RunJobs();
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
