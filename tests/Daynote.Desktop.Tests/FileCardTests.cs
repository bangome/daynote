using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Daynote.App.Localization;
using Daynote.App.Notes;
using Daynote.App.Shell.Product;
using Daynote.Core.Domain;
using Daynote.Core.Files;
using Daynote.Desktop.ViewModels;
using Daynote.Desktop.Views;

namespace Daynote.Desktop.Tests;

/// <summary>
/// The attachment card's "still downloading" state (docs/CLOUD_SYNC.md §5.5).
/// </summary>
/// <remarks>
/// Rendered rather than asserted on the view model, because the view model side is trivially true
/// and the part that can silently be wrong is the markup: the label is bound through
/// <c>Strings[FileAwaitingDownload]</c>, and a compiled binding does not check that the key inside
/// an indexer exists. A missing key renders as empty, which looks exactly like the state not
/// happening.
/// </remarks>
[TestClass]
public sealed class FileCardTests
{
    [TestMethod]
    public void An_attachment_whose_bytes_have_not_arrived_says_so_on_the_card()
    {
        TestServices.WithInitialisedShell((window, shell) =>
        {
            shell.ActiveTab = RightTab.Files;
            shell.Files.Items.Add(Card(available: false));
            Settle(window);

            Assert.IsTrue(
                Texts(window).Contains(AppStrings.FileAwaitingDownload, StringComparer.Ordinal),
                "The card does not show the downloading line.");
        });
    }

    [TestMethod]
    public void An_attachment_that_is_here_shows_no_such_line()
    {
        // The other half of the pair: without it a label that renders unconditionally would pass
        // the test above and be wrong on every card.
        TestServices.WithInitialisedShell((window, shell) =>
        {
            shell.ActiveTab = RightTab.Files;
            shell.Files.Items.Add(Card(available: true));
            Settle(window);

            Assert.IsFalse(
                Texts(window).Contains(AppStrings.FileAwaitingDownload, StringComparer.Ordinal),
                "A ready attachment is labelled as still downloading.");
        });
    }

    [TestMethod]
    public void Saving_a_copy_is_unavailable_until_the_bytes_are_here()
    {
        // Otherwise the file dialog opens and the save fails afterwards, with nothing having said
        // why — the failure the card's line exists to pre-empt.
        FileItemViewModel pending = Card(available: false);
        FileItemViewModel ready = Card(available: true);

        Assert.IsFalse(pending.SaveCommand.CanExecute(null));
        Assert.IsTrue(ready.SaveCommand.CanExecute(null));
    }

    [TestMethod]
    public void A_tag_row_carries_exactly_one_hash()
    {
        // Found in a Store screenshot, not by a test: the panel rendered "##회의", because
        // TagItemViewModel.Tag already includes the leading '#' and this shell's markup added a
        // second one with StringFormat. The WPF shell binds it plainly; only this one did not.
        TestServices.WithInitialisedShell((window, shell) =>
        {
            shell.ActiveTab = RightTab.Tags;
            shell.TagPanel.Tags.Add(new TagItemViewModel(
                new TagSummary("회의", 1, []),
                static _ => Task.CompletedTask));
            Settle(window);

            string[] rows = [.. Texts(window).Where(static text => text.Contains('#', StringComparison.Ordinal))];
            Assert.IsFalse(
                rows.Any(static text => text.Contains("##", StringComparison.Ordinal)),
                $"A tag row has a doubled hash: {string.Join(" | ", rows)}");
            Assert.IsTrue(rows.Contains("#회의", StringComparer.Ordinal), "The tag row is not there at all.");
        });
    }

    private static FileItemViewModel Card(bool available) => new(
        new DayFile(
            Guid.NewGuid(),
            LocalDate.Parse("2026-09-10").Value,
            "보고서.pdf",
            2048,
            "abc123",
            Path.Combine("ab", "abc123.pdf"),
            DateTimeOffset.UtcNow,
            IsAvailable: available),
        thumbnail: null,
        onDelete: static _ => Task.CompletedTask,
        onSave: static _ => Task.CompletedTask);

    /// <summary>Lets the template materialise and lay out before the tree is read.</summary>
    private static void Settle(Window window)
    {
        for (int pass = 0; pass < 10; pass += 1)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    /// <summary>Every visible run of text in the window, as a reader would see it.</summary>
    private static string[] Texts(Window window) =>
    [
        .. window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(static block => block.IsEffectivelyVisible)
            .Select(static block => block.Text)
            .OfType<string>(),
    ];
}
