using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Daynote.App.Account;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.App.Shell.Product;
using Daynote.Core.Files;
using Daynote.Core.Sync;
using Daynote.Infrastructure.Sync;
using Daynote.Mobile.Composition;
using Daynote.Mobile.ViewModels;
using Daynote.Mobile.Views;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// Attachments on the phone: attaching through the picker into the same store and outbox the
/// desktop writes, the size cap, opening a picture in the viewer and anything else in another app,
/// the delete that asks first and syncs as a tombstone, and the one neutral line an account whose
/// files do not sync gets instead of anything to buy.
/// </summary>
[TestClass]
public sealed class FileAttachmentTests
{
    [TestMethod]
    public void An_attached_file_keeps_its_name_and_waits_to_upload() => WithShell((shell, harness) =>
    {
        harness.Account.SignedInEmail = "someone@example.com";
        string picked = harness.Stage("영수증.pdf", 4_000);
        harness.Picker.Next = [picked];

        Pump(() => shell.AttachFilesFromPickerCommand.ExecuteAsync(null));

        MobileFileRowViewModel row = shell.DayFiles.Single();
        Assert.AreEqual("영수증.pdf", row.Name, "The row shows a staging name instead of the picked one.");
        Assert.IsFalse(File.Exists(picked), "The staged copy was left in the cache after the store took it.");
        Assert.IsTrue(row.IsUploadPending, "A file not yet pushed looks the same as one in the cloud.");
        StringAssert.Contains(row.Detail, MobileStrings.Get("MobileFileUploadPending"));

        IReadOnlySet<string> queued = Result(harness.SyncStore.ReadQueuedFileIdsAsync().AsTask());
        Assert.Contains(row.Item.Id.ToString("D"), queued, "The attachment did not reach the sync outbox.");
    });

    [TestMethod]
    public void A_photo_comes_from_the_photo_picker() => WithShell((shell, harness) =>
    {
        harness.Picker.NextPhotos = [harness.Stage("바다.png", ScreenshotTests.SamplePng(40, 30))];

        Pump(() => shell.AttachPhotosCommand.ExecuteAsync(null));

        Assert.AreEqual(1, harness.Picker.PhotoCalls);
        Assert.AreEqual(0, harness.Picker.FileCalls);
        Assert.IsTrue(shell.DayFiles.Single().HasThumbnail, "A photo row has no thumbnail.");
    });

    [TestMethod]
    public void A_file_over_the_cap_is_refused_with_a_reason() => WithShell((shell, harness) =>
    {
        string huge = harness.Stage("video.mov", 0);
        using (FileStream stream = File.OpenWrite(huge))
        {
            stream.SetLength(FileCapturePolicy.MaxFileBytes + 1);
        }

        harness.Picker.Next = [huge];
        Pump(() => shell.AttachFilesFromPickerCommand.ExecuteAsync(null));

        Assert.IsEmpty(shell.DayFiles);
        Assert.IsTrue(shell.HasFilesNotice);
        StringAssert.Contains(shell.FilesNotice, "256");
    });

    [TestMethod]
    public void A_picture_opens_in_the_viewer_and_the_back_gesture_closes_it() => WithShell((shell, harness) =>
    {
        harness.Picker.Next = [harness.Stage("사진.png", ScreenshotTests.SamplePng(64, 48))];
        Pump(() => shell.AttachFilesFromPickerCommand.ExecuteAsync(null));

        Pump(() => shell.DayFiles.Single().OpenCommand.ExecuteAsync(null));

        Assert.IsTrue(shell.IsImageViewerOpen);
        Assert.IsNotNull(shell.ViewerImage);
        Assert.AreEqual("사진.png", shell.ViewerTitle);
        Assert.IsFalse(shell.ShowDock, "The tab bar shows over the image.");
        Assert.IsNull(harness.Opened, "A picture was handed to another app instead of the viewer.");

        Assert.IsTrue(Result(shell.GoBackAsync()));
        Assert.IsFalse(shell.IsImageViewerOpen);
        Assert.IsNull(shell.ViewerImage, "The decoded picture outlived the viewer.");
    });

    [TestMethod]
    public void Another_file_is_handed_to_another_app_with_its_name_and_bytes() => WithShell((shell, harness) =>
    {
        harness.Picker.Next = [harness.Stage("계약서.pdf", 1_234)];
        Pump(() => shell.AttachFilesFromPickerCommand.ExecuteAsync(null));

        Pump(() => shell.DayFiles.Single().OpenCommand.ExecuteAsync(null));

        Assert.IsNotNull(harness.Opened);
        Assert.AreEqual("계약서.pdf", harness.Opened.Value.Name);
        Assert.HasCount(1_234, harness.Opened.Value.Bytes);
        Assert.IsFalse(shell.IsImageViewerOpen);
    });

    [TestMethod]
    public void With_no_app_to_open_it_a_copy_is_offered() => WithShell((shell, harness) =>
    {
        harness.OpenResult = false;
        harness.Picker.Next = [harness.Stage("data.xyz", 99)];
        string destination = Path.Combine(harness.Root, "saved-copy.xyz");
        harness.Picker.SavePath = destination;
        Pump(() => shell.AttachFilesFromPickerCommand.ExecuteAsync(null));

        Pump(() => shell.DayFiles.Single().OpenCommand.ExecuteAsync(null));

        Assert.IsTrue(File.Exists(destination), "No copy was saved when no app could open the file.");
        Assert.AreEqual(destination, harness.Picker.Completed, "A staged save was never forwarded to its destination.");
        Assert.AreEqual(MobileStrings.Get("MobileFileCopySaved"), shell.DayFiles.Single().Detail);
    });

    [TestMethod]
    public void Deleting_asks_first_and_leaves_a_tombstone_to_sync() => WithShell((shell, harness) =>
    {
        harness.Picker.Next = [harness.Stage("지울 파일.txt", 10)];
        Pump(() => shell.AttachFilesFromPickerCommand.ExecuteAsync(null));
        MobileFileRowViewModel row = shell.DayFiles.Single();

        row.ShowMenuCommand.Execute(null);
        Assert.IsTrue(shell.IsFileMenuOpen);
        Assert.IsFalse(shell.IsConfirmingDelete, "The delete question came before anyone asked to delete.");
        Assert.IsFalse(shell.ShowDock);

        shell.RequestDeleteFileCommand.Execute(null);
        Assert.IsTrue(shell.IsConfirmingDelete);
        Assert.HasCount(1, shell.DayFiles, "Asking to delete deleted.");

        Pump(() => shell.ConfirmDeleteFileCommand.ExecuteAsync(null));

        Assert.IsEmpty(shell.DayFiles);
        Assert.IsFalse(shell.IsFileMenuOpen);
        using var connection = new SqliteConnection($"Data Source={harness.DatabasePath};Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sync_tombstones WHERE entity = 'file' AND entity_id = $id;";
        command.Parameters.AddWithValue("$id", row.Item.Id.ToString("D"));
        Assert.AreEqual(1L, (long)command.ExecuteScalar()!, "The delete left no tombstone, so other devices keep the file.");
    });

    [TestMethod]
    public void Lifting_the_finger_after_a_long_press_does_not_open_the_file() => WithShell((shell, harness) =>
    {
        harness.Picker.Next = [harness.Stage("사진.png", ScreenshotTests.SamplePng(64, 48))];
        Pump(() => shell.AttachFilesFromPickerCommand.ExecuteAsync(null));
        MobileFileRowViewModel row = shell.DayFiles.Single();

        // The press raises the menu; the release then taps the row underneath.
        row.ShowMenuCommand.Execute(null);
        Pump(() => row.OpenCommand.ExecuteAsync(null));

        Assert.IsTrue(shell.IsFileMenuOpen, "The release closed the menu the press opened.");
        Assert.IsFalse(shell.IsImageViewerOpen, "The release opened the picture under the menu.");
    });

    [TestMethod]
    public void Backing_out_of_the_menu_keeps_the_file() => WithShell((shell, harness) =>
    {
        harness.Picker.Next = [harness.Stage("남길 파일.txt", 10)];
        Pump(() => shell.AttachFilesFromPickerCommand.ExecuteAsync(null));
        shell.DayFiles.Single().ShowMenuCommand.Execute(null);
        shell.RequestDeleteFileCommand.Execute(null);

        Assert.IsTrue(Result(shell.GoBackAsync()));

        Assert.IsFalse(shell.IsFileMenuOpen);
        Assert.HasCount(1, shell.DayFiles);
    });

    [TestMethod]
    public void An_account_without_file_sync_is_told_so_and_offered_nothing_to_buy() => WithShell((shell, harness) =>
    {
        harness.Picker.Next = [harness.Stage("메모.txt", 10)];
        Pump(() => shell.AttachFilesFromPickerCommand.ExecuteAsync(null));
        MobileFileRowViewModel row = shell.DayFiles.Single();

        // Signed out: the file is local, and says so.
        Assert.AreEqual(MobileStrings.Get("MobileFilesLocalOnly"), shell.FilesSyncNote);
        Assert.IsFalse(row.IsUploadPending, "A signed-out device says a file is waiting for an upload that will not come.");

        harness.Account.SignedInEmail = "someone@example.com";
        Dispatcher.UIThread.RunJobs();
        Assert.IsFalse(shell.HasFilesSyncNote, "A trial account was told its files do not sync.");

        harness.Account.Entitlement = new Entitlement(EntitlementState.Expired, null, CanSyncFiles: false, HasSubscribed: false);
        Assert.AreEqual(MobileStrings.Get("MobileFilesNotSynced"), shell.FilesSyncNote);
        Assert.IsFalse(row.IsUploadPending, "Every row repeats what the note already says.");

        harness.Account.Entitlement = new Entitlement(
            EntitlementState.Active, null, true, true, BillingTier.Pro, BillingPlan.Annual, QuotaBytes: 2L << 30, UsedBytes: 2L << 30);
        Assert.AreEqual(MobileStrings.Get("MobileFilesQuotaFull"), shell.FilesSyncNote);

        // App Store 3.1.1 and Play's payments policy: no price, no plan name, no pointer to buying.
        foreach (string note in new[] { "MobileFilesLocalOnly", "MobileFilesNotSynced", "MobileFilesQuotaFull" }
            .SelectMany(key => new[] { MobileCatalog.Korean[key], MobileCatalog.English[key] }))
        {
            foreach (string forbidden in new[] { "구독", "구매", "결제", "업그레이드", "Pro", "Premium", "₩", "$", "subscri", "upgrade", "buy", "purchase", "http" })
            {
                Assert.DoesNotContain(forbidden, note, $"The note '{note}' points at a purchase.");
            }
        }
    });

    // ── Harness ─────────────────────────────────────────────────────────────────────────────────

    private sealed class Harness
    {
        public required string Root { get; init; }

        public required string DatabasePath { get; init; }

        public required AccountViewModel Account { get; init; }

        public required SqliteSyncStore SyncStore { get; init; }

        public required FakePicker Picker { get; init; }

        public (string Name, byte[] Bytes)? Opened { get; set; }

        public bool OpenResult { get; set; } = true;

        /// <summary>A picked file, where the real picker leaves its copies.</summary>
        public string Stage(string name, int length) => Stage(name, new byte[length]);

        public string Stage(string name, byte[] bytes)
        {
            string slot = Path.Combine(Picker.StagingDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(slot);
            string path = Path.Combine(slot, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }
    }

    private sealed class FakePicker : IFilePicker, IPhotoPicker
    {
        public IReadOnlyList<string> Next { get; set; } = [];

        public IReadOnlyList<string> NextPhotos { get; set; } = [];

        public string? SavePath { get; set; }

        public string? Completed { get; private set; }

        public int FileCalls { get; private set; }

        public int PhotoCalls { get; private set; }

        public string StagingDirectory { get; } = Path.Combine(Path.GetTempPath(), "daynote-mobile-tests", "staging-" + Guid.NewGuid().ToString("N"));

        public Task<IReadOnlyList<string>> PickFilesAsync(CancellationToken cancellationToken = default)
        {
            FileCalls++;
            return Task.FromResult(Next);
        }

        public Task<IReadOnlyList<string>> PickPhotosAsync(CancellationToken cancellationToken = default)
        {
            PhotoCalls++;
            return Task.FromResult(NextPhotos);
        }

        public Task<string?> PickSavePathAsync(string suggestedFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult(SavePath);

        public Task CompleteSaveAsync(string path, CancellationToken cancellationToken = default)
        {
            Completed = path;
            return Task.CompletedTask;
        }
    }

    private static void WithShell(Action<MobileShellViewModel, Harness> body)
    {
        string root = Path.Combine(Path.GetTempPath(), "daynote-mobile-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            HeadlessAppFixture.OnUiThread(() =>
            {
                LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
                Harness? harness = null;
                var options = new DaynoteAppOptions(root) { SyncEndpoint = new Uri("https://sync.invalid") };
                var services = new ServiceCollection();
                services.AddDaynoteMobile(
                    options,
                    Application.Current!,
                    () => null,
                    TestServices.PlatformFor(root) with
                    {
                        SecretProtector = new AccountPanelTests.XorProtector(),
                        Identity = new AccountPanelTests.NoGoogle(),
                        OpenFile = (name, bytes) =>
                        {
                            harness!.Opened = (name, bytes);
                            return Task.FromResult(harness.OpenResult);
                        },
                    });
                var picker = new FakePicker();
                services.AddSingleton<IFilePicker>(picker);
                ServiceProvider provider = services.BuildServiceProvider();
                try
                {
                    var shell = provider.GetRequiredService<MobileShellViewModel>();
                    harness = new Harness
                    {
                        Root = root,
                        DatabasePath = options.DatabasePath,
                        Account = shell.Account!,
                        SyncStore = (SqliteSyncStore)provider.GetRequiredService<ISyncStore>(),
                        Picker = picker,
                    };

                    var view = new MainView { DataContext = shell };
                    var host = new Window { Content = view, Width = 390, Height = 844 };
                    host.Show();
                    Pump(() => shell.InitializeAsync());
                    body(shell, harness);
                    host.Close();
                }
                finally
                {
                    provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            });
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static T Result<T>(Task<T> task)
    {
        Pump(() => task);
        return task.GetAwaiter().GetResult();
    }

    private static void Pump(Func<Task> work) => ScreenshotTests.Pump(work);
}
