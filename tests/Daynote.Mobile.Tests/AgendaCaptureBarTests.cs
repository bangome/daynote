using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Daynote.Core.Agenda;
using Daynote.Mobile.ViewModels;
using Daynote.Mobile.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// The phone's <c>@</c> bar (phone §01): what opens it, what it reads back, and what 만들기 makes.
/// </summary>
/// <remarks>
/// Driven through the body box rather than the view model, because the whole point of the bar is
/// that it follows the caret: the view is the only thing that knows where the caret is, and a test
/// that calls <c>UpdateCapture</c> itself would pass with that wiring cut.
/// </remarks>
[TestClass]
public sealed class AgendaCaptureBarTests
{
    [TestMethod]
    public void Typing_a_date_after_an_at_opens_the_bar_over_the_helpers()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            TextBox body = OpenEditor(view, shell);

            Type(body, "회의자료 초안 공유 @내일");

            Assert.IsTrue(shell.Capture.IsOpen);
            Assert.IsFalse(shell.Capture.IsPrompting);
            Assert.AreEqual("회의자료 초안 공유", shell.Capture.Title, "The title is already typed; the bar shows it.");
            Assert.IsTrue(shell.Capture.TaskLine.Length > 0);
            Assert.IsTrue(shell.Capture.EventLine.Length > 0);
            Assert.IsTrue(shell.Capture.IsTaskSelected, "A to-do is what an @ usually means.");

            // The helpers are gone while the bar is there: one accessory slot, one occupant.
            Assert.IsFalse(view.GetVisualDescendants().OfType<Button>()
                .Any(button => button.Name == "AttachTool" && button.IsEffectivelyVisible));
        });
    }

    [TestMethod]
    public void An_at_that_is_not_a_date_never_opens_it()
    {
        // "자료는 @지원 님께 전달" - @ stays an ordinary character, and a bar that flickered up on a
        // name would make the editor feel like it was guessing.
        TestServices.WithInitialisedShell((view, shell) =>
        {
            TextBox body = OpenEditor(view, shell);

            Type(body, "자료는 @지원 님께 전달");

            Assert.IsFalse(shell.Capture.IsOpen);
        });
    }

    [TestMethod]
    public void Right_after_the_at_it_invites_rather_than_complains()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            TextBox body = OpenEditor(view, shell);

            Type(body, "분기 회고 잡기 @");

            Assert.IsTrue(shell.Capture.IsOpen);
            Assert.IsTrue(shell.Capture.IsPrompting);
            Assert.HasCount(4, shell.Capture.Examples);
            Assert.IsTrue(shell.Capture.Prompt.Length > 0);

            // Enter is still a line break: swallowing it the moment an @ was typed would make the
            // editor feel stuck.
            PressEnter(body);
            Assert.IsEmpty(Items(shell), "Enter made something before anything had been read.");

            // The line break went in, which also ends the phrase: a phrase does not span lines,
            // so the bar closes behind it rather than reading the next line as a date.
            Assert.EndsWith("\n", body.Text!);
            Assert.IsFalse(shell.Capture.IsOpen);
        });
    }

    [TestMethod]
    public void The_return_key_makes_what_the_selected_line_says()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            TextBox body = OpenEditor(view, shell);
            Type(body, "회의자료 초안 공유 @내일");
            string typed = body.Text!;

            PressEnter(body);
            Pump(() => shell.CommitCaptureCommand.ExecutionTask ?? Task.CompletedTask);

            AgendaItem made = Items(shell).Single();
            Assert.AreEqual(AgendaKind.Task, made.Kind);
            Assert.AreEqual("회의자료 초안 공유", made.Title);
            Assert.AreEqual(DateOnly.FromDateTime(DateTime.Now.AddDays(1)), DateOnly.FromDateTime(made.DueAt!.Value.Value));
            Assert.IsFalse(shell.Capture.IsOpen);

            // §7: what stays in the note is exactly what was typed. Nothing is cut out, so an item
            // made by mistake is a row to delete rather than an edit to undo.
            Assert.AreEqual(typed, body.Text);
        });
    }

    [TestMethod]
    public void Tapping_the_other_line_changes_the_shape_not_the_date()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            TextBox body = OpenEditor(view, shell);
            Type(body, "디자인 리뷰 준비 @내일");

            shell.SelectCaptureKindCommand.Execute(AgendaKind.Event);
            Assert.IsTrue(shell.Capture.IsEventSelected);
            Assert.AreEqual(shell.Capture.EventLine, shell.Capture.SelectedLine);

            Pump(() => shell.CommitCaptureCommand.ExecuteAsync(null));

            AgendaItem made = Items(shell).Single();
            Assert.AreEqual(AgendaKind.Event, made.Kind);
            Assert.IsNull(made.DueAt, "An event occupies time; it is not owed.");
            Assert.AreEqual(DateOnly.FromDateTime(DateTime.Now.AddDays(1)), DateOnly.FromDateTime(made.StartsAt!.Value.Value));
        });
    }

    [TestMethod]
    public void A_made_item_is_in_the_lists_without_reopening_anything()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            TextBox body = OpenEditor(view, shell);
            Type(body, "퇴근 전 로그 확인 @오늘");

            Pump(() => shell.CommitCaptureCommand.ExecuteAsync(null));

            Assert.IsTrue(
                shell.TodoGroups.SelectMany(group => group.Items).Any(row => row.Item.Text == "퇴근 전 로그 확인"),
                "The to-do was written but the screens were never told to re-read.");
        });
    }

    [TestMethod]
    public void Dismissing_makes_nothing_and_keeps_the_text()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            TextBox body = OpenEditor(view, shell);
            Type(body, "서버 점검 공지 @내일");
            string typed = body.Text!;

            shell.DismissCaptureCommand.Execute(null);

            Assert.IsFalse(shell.Capture.IsOpen);
            Assert.AreEqual(typed, body.Text);
            Assert.IsEmpty(Items(shell));
        });
    }

    [TestMethod]
    public void An_example_chip_types_itself()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            TextBox body = OpenEditor(view, shell);
            Type(body, "분기 회고 잡기 @");

            Button chip = view.GetVisualDescendants().OfType<Button>()
                .First(button => ReferenceEquals(button.DataContext, shell.Capture.Examples[1]));
            chip.Command?.Execute(chip.CommandParameter);
            chip.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.EndsWith(shell.Capture.Examples[1], body.Text!);
            Assert.IsFalse(shell.Capture.IsPrompting, "What the chip typed should have been read back.");
        });
    }

    [TestMethod]
    public void A_short_screen_folds_the_bar_to_one_line()
    {
        // §01 ⑤: two readings do not fit above a keyboard on a small phone, and a readback you
        // have to scroll to is one nobody reads.
        TestServices.WithInitialisedShell(320, 480, (view, shell) =>
        {
            TextBox body = OpenEditor(view, shell);
            view.GetVisualDescendants().OfType<EditorPage>().Single().SetBottomInset(0, keyboard: 300);
            Dispatcher.UIThread.RunJobs();
            Type(body, "업체에 전화 @내일");

            Assert.IsTrue(shell.IsCaptureBarCompact);
            Assert.AreEqual(shell.Capture.TaskLine, shell.Capture.SelectedLine);
        });
    }

    /// <summary>Opens a new note and hands back its body box.</summary>
    private static TextBox OpenEditor(MainView view, MobileShellViewModel shell)
    {
        Pump(() => shell.NewNoteCommand.ExecuteAsync(null));
        return view.GetVisualDescendants().OfType<EditorPage>().Single()
            .GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "Body");
    }

    /// <summary>Types into the body the way a keyboard does: text, then the caret after it.</summary>
    private static void Type(TextBox body, string text)
    {
        body.Text = text;
        body.CaretIndex = text.Length;
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// The return key on the body. Whether it was handled says nothing — the box handles Enter
    /// itself to put a line break in — so what it did is read from what exists afterwards.
    /// </summary>
    private static void PressEnter(TextBox body)
    {
        body.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();
    }

    private static IReadOnlyList<AgendaItem> Items(MobileShellViewModel shell)
    {
        var agenda = (IAgendaRepository)TestServices.CurrentProvider!.GetService(typeof(IAgendaRepository))!;
        IReadOnlyList<AgendaItem> items = [];
        Pump(async () => items = await agenda.GetAllAsync().ConfigureAwait(true));
        return items;
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
