using Avalonia;
using Avalonia.Threading;
using Daynote.App.Account;
using Daynote.App.Composition;
using Daynote.App.Notes;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Sync;
using Daynote.Infrastructure.Sync;
using Daynote.Mobile.Composition;
using Daynote.Mobile.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// What automatic sync does on a phone. The pull is faked through the sync store's merge, which is
/// what the real engine writes with; the shell, the day list and the editor layer are real.
/// </summary>
[TestClass]
public sealed class AutoSyncTests
{
    [TestMethod]
    public void A_note_from_another_device_appears_in_the_day_list() => WithSignedInShell((shell, pull) =>
    {
        pull.Next = "FROM-THE-MAC";

        shell.StartAutoSync();

        PumpUntil(() => shell.Notes.Tabs.Any(tab => tab.Body == "FROM-THE-MAC"), "the pulled note never reached the day list");
    });

    [TestMethod]
    public void The_editor_closes_when_its_note_is_deleted_elsewhere() => WithSignedInShell((shell, pull) =>
    {
        pull.Next = "SOON-DELETED";
        shell.StartAutoSync();
        PumpUntil(() => shell.Notes.Tabs.Any(tab => tab.Body == "SOON-DELETED"), "the note never arrived");
        NoteTabViewModel doomed = shell.Notes.Tabs.First(tab => tab.Body == "SOON-DELETED");
        shell.OpenNoteCommand.Execute(doomed);
        PumpUntil(() => shell.IsEditorOpen, "the editor did not open");

        pull.DeleteNext = doomed.Id;
        Task manual = shell.Account!.SyncCommand.ExecuteAsync(null);
        PumpUntil(() => manual.IsCompleted, "the manual sync did not finish");
        manual.GetAwaiter().GetResult();

        PumpUntil(() => !shell.IsEditorOpen, "the editor stayed up on a note that no longer exists");
        Assert.IsTrue(shell.Notes.Tabs.All(tab => tab.Body != "SOON-DELETED"));
    });

    [TestMethod]
    public void The_editor_stays_open_when_another_note_arrives() => WithSignedInShell((shell, pull) =>
    {
        pull.Next = "FIRST";
        shell.StartAutoSync();
        PumpUntil(() => shell.Notes.Tabs.Any(tab => tab.Body == "FIRST"), "the note never arrived");
        NoteTabViewModel open = shell.Notes.Tabs.First(tab => tab.Body == "FIRST");
        shell.OpenNoteCommand.Execute(open);
        PumpUntil(() => shell.IsEditorOpen, "the editor did not open");

        pull.Next = "SECOND";
        Task manual = shell.Account!.SyncCommand.ExecuteAsync(null);
        PumpUntil(() => manual.IsCompleted, "the manual sync did not finish");
        manual.GetAwaiter().GetResult();
        PumpUntil(() => shell.Notes.Tabs.Any(tab => tab.Body == "SECOND"), "the second note never arrived");

        Assert.IsTrue(shell.IsEditorOpen, "An unrelated arrival closed the editor.");
        Assert.AreEqual(open.Id, shell.Notes.SelectedTab?.Id, "The editor switched to another note.");
    });

    private static void WithSignedInShell(Action<MobileShellViewModel, FakePull> body)
    {
        string root = Path.Combine(Path.GetTempPath(), "daynote-mobile-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            HeadlessAppFixture.OnUiThread(() =>
            {
                // The real phone graph with sync registered: an endpoint, a sign-in that is never
                // used, and a protector that keeps the run off the machine's keychain.
                var services = new ServiceCollection();
                services.AddDaynoteMobile(
                    new DaynoteAppOptions(root) { SyncEndpoint = new Uri("https://sync.invalid") },
                    Application.Current!,
                    () => null,
                    TestServices.PlatformFor(root) with { SecretProtector = new XorProtector(), Identity = new NoIdentity() });
                ServiceProvider provider = services.BuildServiceProvider();
                try
                {
                    var shell = provider.GetRequiredService<MobileShellViewModel>();
                    Task initialising = shell.InitializeAsync();
                    PumpUntil(() => initialising.IsCompleted, "the shell did not initialise");
                    initialising.GetAwaiter().GetResult();

                    var pull = new FakePull(provider.GetRequiredService<ISyncStore>(), shell.SelectedDate);
                    shell.Account = new AccountViewModel(
                        provider.GetRequiredService<AccountService>(),
                        provider.GetRequiredService<ISyncStore>(),
                        pull.RunAsync,
                        new NoExport(),
                        _ => { },
                        Path.Combine(root, "conflicts"))
                    {
                        SignedInEmail = "someone@example.com",
                    };

                    body(shell, pull);
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

    private static void PumpUntil(Func<bool> condition, string failure)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, $"Timed out: {failure}.");
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }

    /// <summary>One sync run that writes through the sync store's merge, as a real pull does.</summary>
    private sealed class FakePull(ISyncStore store, LocalDate date)
    {
        private DateTimeOffset stamp = DateTimeOffset.UtcNow.AddMinutes(1);

        public string? Next { get; set; }

        public NoteId? DeleteNext { get; set; }

        public async ValueTask<SyncReport> RunAsync()
        {
            SyncNote[] notes = Next is { } body
                ? [new SyncNote(Guid.NewGuid().ToString("D"), date, "From another device", body, 99, false, true, [], stamp, stamp)]
                : [];
            SyncTombstone[] deletes = DeleteNext is { } id ? [new SyncTombstone(SyncEntityKind.Note, id.ToString(), stamp)] : [];
            Next = null;
            DeleteNext = null;
            stamp = stamp.AddSeconds(1);

            MergeOutcome merged = await store.MergeNotesAsync(notes, deletes);
            return SyncReport.For(SyncOutcome.Completed) with
            {
                Pulled = notes.Length + deletes.Length,
                Applied = merged.Applied,
                Deleted = merged.Deleted,
            };
        }
    }

    private sealed class XorProtector : ISecretProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> entropy) => Xor(plaintext);

        public byte[] Unprotect(ReadOnlySpan<byte> sealedBytes, ReadOnlySpan<byte> entropy) => Xor(sealedBytes);

        private static byte[] Xor(ReadOnlySpan<byte> input)
        {
            byte[] output = input.ToArray();
            for (int i = 0; i < output.Length; i++)
            {
                output[i] ^= 0x5A;
            }

            return output;
        }
    }

    private sealed class NoIdentity : IIdentityProvider
    {
        public ValueTask<IdentityGrant> AuthorizeAsync(CancellationToken cancellationToken = default) =>
            throw new AccountException(AccountFailure.SignInCancelled, "Not used by these tests.");
    }

    private sealed class NoExport : IRecoveryKeyExporter
    {
        public Task<bool> TryCopyToClipboardAsync(string recoveryKey) => Task.FromResult(false);

        public Task<bool> TrySaveToFileAsync(string recoveryKey) => Task.FromResult(false);
    }
}
