using System.IO;
using Daynote.App.Shell.Product;
using Daynote.App.Tests.Workspace;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Product;

/// <summary>
/// Double-clicking an attachment writes a copy wherever the user points.
/// </summary>
/// <remarks>
/// The bytes live in the content-addressed store under a hashed name, so before this there was no way
/// to get an attachment back out of the app — "open the containing folder" would have shown the user
/// a directory of hashes. The whole path is exercised here against the real repository and store:
/// import a file, ask the card to save, and read back what landed on disk.
/// </remarks>
[TestClass]
public sealed class FileSaveTests
{
    [TestMethod]
    public async Task Saving_a_card_writes_the_stored_bytes_to_the_chosen_path()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await using (harness)
        {
            byte[] payload = [1, 2, 3, 4, 250, 251, 252];
            string source = context.WriteScratchFile("report.bin", payload);
            harness.Picker.Paths.Add(source);

            await harness.Shell.Files.LoadForDateAsync(WorkspaceTestContext.Date("2026-07-20"));
            await harness.Shell.Files.AddFilesCommand.ExecuteAsync(null);
            FileItemViewModel card = harness.Shell.Files.Items.Single();
            Assert.AreEqual("report.bin", card.Name);

            string destination = context.ScratchPath("copies", "report-copy.bin");
            harness.Picker.SavePath = destination;


            await card.SaveCommand.ExecuteAsync(null);

            Assert.IsTrue(File.Exists(destination), "Nothing was written to the chosen path.");
            CollectionAssert.AreEqual(payload, await File.ReadAllBytesAsync(destination), "The copy is not the stored file.");
            Assert.AreEqual(destination, card.SavedTo, "The card does not say where the copy went.");
            Assert.IsTrue(card.WasSaved);
            Assert.IsFalse(card.SaveFailed);

            // The dialog is seeded with the attachment's own name, not a hash or a placeholder.
            CollectionAssert.AreEqual(new[] { "report.bin" }, harness.Picker.SuggestedNames);
        }
    }

    [TestMethod]
    public async Task Cancelling_the_dialog_writes_nothing_and_reports_nothing()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await using (harness)
        {
            harness.Picker.Paths.Add(context.WriteScratchFile("memo.txt", [65, 66]));
            await harness.Shell.Files.LoadForDateAsync(WorkspaceTestContext.Date("2026-07-20"));
            await harness.Shell.Files.AddFilesCommand.ExecuteAsync(null);

            FileItemViewModel card = harness.Shell.Files.Items.Single();
            harness.Picker.SavePath = null;   // the user closed the dialog

            await card.SaveCommand.ExecuteAsync(null);

            Assert.IsFalse(card.WasSaved, "Cancelling should not claim a file was saved.");
            Assert.IsFalse(card.SaveFailed, "Cancelling is a decision, not a failure.");
        }
    }

    [TestMethod]
    public async Task An_unwritable_destination_is_reported_on_the_card()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await using (harness)
        {
            harness.Picker.Paths.Add(context.WriteScratchFile("memo.txt", [65, 66]));
            await harness.Shell.Files.LoadForDateAsync(WorkspaceTestContext.Date("2026-07-20"));
            await harness.Shell.Files.AddFilesCommand.ExecuteAsync(null);

            FileItemViewModel card = harness.Shell.Files.Items.Single();

            // A directory is never a writable file path, on any OS, without needing a permission setup.
            harness.Picker.SavePath = context.ScratchPath("a-directory");
            Directory.CreateDirectory(harness.Picker.SavePath!);

            await card.SaveCommand.ExecuteAsync(null);

            Assert.IsTrue(card.SaveFailed, "A write that cannot succeed must say so rather than fail silently.");
            Assert.IsFalse(card.WasSaved);
        }
    }
}
