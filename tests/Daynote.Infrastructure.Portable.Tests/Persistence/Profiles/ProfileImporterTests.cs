using Daynote.Core.Settings;
using Daynote.Infrastructure.Persistence;
using Daynote.Infrastructure.Persistence.Profiles;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Daynote.Infrastructure.Portable.Tests.Persistence.Profiles.ProfileFixture;

namespace Daynote.Infrastructure.Portable.Tests.Persistence.Profiles;

/// <summary>docs/PROFILES.md §5.2 step 4 and §5.5: bringing one profile's notes into another.</summary>
[TestClass]
public sealed class ProfileImporterTests
{
    [TestMethod]
    public async Task Notes_arrive_with_tags_titles_favourites_order_and_dates_and_are_queued_for_push()
    {
        using var root = new TempDirectory();
        string source = Path.Combine(root.Path, "src");
        string destination = Path.Combine(root.Path, "dst");

        SqliteDatabase from = Open(source);
        try
        {
            await InsertNoteAsync(from, NoteId(1), "2026-09-01", "Plan", "first", 0, Utc(1), Utc(3), favorite: true, customTitle: true, "work", "q3");
            await InsertNoteAsync(from, NoteId(2), "2026-09-01", "노트 2", "second", 1, Utc(1), Utc(2));
        }
        finally
        {
            await from.DisposeAsync();
        }

        SqliteDatabase to = Open(destination);
        try
        {
            ProfileImportResult result = await new ProfileImporter(to, destination).ImportFromAsync(source);

            Assert.AreEqual(2, result.NotesImported);
            Assert.AreEqual(0, result.NotesKept);
        }
        finally
        {
            await to.DisposeAsync();
        }

        Assert.AreEqual("Plan", Scalar(destination, "SELECT title FROM notes WHERE id=$id;", ("$id", NoteId(1))));
        Assert.AreEqual("first", Scalar(destination, "SELECT body FROM notes WHERE id=$id;", ("$id", NoteId(1))));
        Assert.AreEqual(1L, Count(destination, "SELECT is_favorite FROM notes WHERE id=$id;", ("$id", NoteId(1))));
        Assert.AreEqual(0L, Count(destination, "SELECT sort_order FROM notes WHERE id=$id;", ("$id", NoteId(1))));
        Assert.AreEqual(1L, Count(destination, "SELECT sort_order FROM notes WHERE id=$id;", ("$id", NoteId(2))));
        Assert.AreEqual(Utc(1), Scalar(destination, "SELECT created_utc FROM notes WHERE id=$id;", ("$id", NoteId(1))));
        Assert.AreEqual(Utc(3), Scalar(destination, "SELECT updated_utc FROM notes WHERE id=$id;", ("$id", NoteId(1))));
        Assert.AreEqual("work q3", Scalar(destination, "SELECT group_concat(tag, ' ') FROM (SELECT tag FROM note_tags WHERE note_id=$id ORDER BY sort_order);", ("$id", NoteId(1))));
        Assert.AreEqual(1L, Count(destination, "SELECT COUNT(*) FROM settings WHERE key=$k;", ("$k", "note.custom-title." + NoteId(1))));
        Assert.AreEqual(0L, Count(destination, "SELECT COUNT(*) FROM settings WHERE key=$k;", ("$k", "note.custom-title." + NoteId(2))));
        Assert.AreEqual(2L, Count(destination, "SELECT COUNT(*) FROM sync_outbox WHERE entity='note';"), "the destination's outbox picks them up");
        Assert.AreEqual(1L, Count(destination, "SELECT COUNT(*) FROM search_documents WHERE source_type='note' AND source_id=$id;", ("$id", NoteId(1))), "searchable");
        Assert.AreEqual(2L, Count(source, "SELECT COUNT(*) FROM notes;"), "the source is left as it is");
    }

    [TestMethod]
    public async Task An_id_on_both_sides_keeps_the_later_version_and_saves_the_loser_as_a_conflict_copy()
    {
        using var root = new TempDirectory();
        string source = Path.Combine(root.Path, "src");
        string destination = Path.Combine(root.Path, "dst");

        SqliteDatabase from = Open(source);
        try
        {
            await InsertNoteAsync(from, NoteId(1), "2026-09-01", "A", "source newer", 0, Utc(1), Utc(5), customTitle: true);
            await InsertNoteAsync(from, NoteId(2), "2026-09-01", "B", "source older", 1, Utc(1), Utc(2), customTitle: true);
        }
        finally
        {
            await from.DisposeAsync();
        }

        SqliteDatabase to = Open(destination);
        try
        {
            await InsertNoteAsync(to, NoteId(1), "2026-09-01", "A", "destination older", 0, Utc(1), Utc(3), customTitle: true);
            await InsertNoteAsync(to, NoteId(2), "2026-09-01", "B", "destination newer", 1, Utc(1), Utc(4), customTitle: true);
            await ExecAsync(to, "DELETE FROM sync_outbox;");

            ProfileImportResult result = await new ProfileImporter(to, destination).ImportFromAsync(source);

            Assert.AreEqual(1, result.NotesImported);
            Assert.AreEqual(1, result.NotesKept);
            Assert.AreEqual(1, result.ConflictCopies);
        }
        finally
        {
            await to.DisposeAsync();
        }

        Assert.AreEqual("source newer", Scalar(destination, "SELECT body FROM notes WHERE id=$id;", ("$id", NoteId(1))));
        Assert.AreEqual("destination newer", Scalar(destination, "SELECT body FROM notes WHERE id=$id;", ("$id", NoteId(2))));
        Assert.AreEqual(1L, Count(destination, "SELECT COUNT(*) FROM sync_outbox WHERE entity='note' AND entity_id=$id;", ("$id", NoteId(1))));
        Assert.AreEqual(0L, Count(destination, "SELECT COUNT(*) FROM sync_outbox WHERE entity='note' AND entity_id=$id;", ("$id", NoteId(2))), "the winner there did not change");
        string[] conflicts = Directory.GetFiles(Path.Combine(destination, "conflicts"));
        Assert.AreEqual(1, conflicts.Length);
        StringAssert.Contains(File.ReadAllText(conflicts[0]), "destination older");
    }

    [TestMethod]
    public async Task Attachments_arrive_with_their_bytes_once_per_content_and_are_queued_for_push()
    {
        using var root = new TempDirectory();
        string source = Path.Combine(root.Path, "src");
        string destination = Path.Combine(root.Path, "dst");
        byte[] content = "quarterly numbers"u8.ToArray();
        string hash;

        SqliteDatabase from = Open(source);
        try
        {
            hash = await InsertFileAsync(from, source, NoteId(10), "2026-09-01", "report.csv", content, Utc(1));
            await InsertFileAsync(from, source, NoteId(11), "2026-09-02", "copy.csv", content, Utc(2));
            await InsertFileAsync(from, source, NoteId(12), "2026-09-02", "never-downloaded.pdf", "x"u8.ToArray(), Utc(2), writeBlob: false);
            await InsertFileAsync(from, source, NoteId(13), "2026-09-03", "shared.txt", "both"u8.ToArray(), Utc(3));
        }
        finally
        {
            await from.DisposeAsync();
        }

        SqliteDatabase to = Open(destination);
        try
        {
            await InsertFileAsync(to, destination, NoteId(13), "2026-09-03", "shared.txt", "both"u8.ToArray(), Utc(3));

            ProfileImportResult result = await new ProfileImporter(to, destination).ImportFromAsync(source);

            Assert.AreEqual(2, result.FilesImported);
            Assert.AreEqual(1, result.FilesAlreadyPresent);
            Assert.AreEqual(1, result.FilesSkipped, "no bytes to copy");
            Assert.AreEqual(1, result.BlobsCopied, "identical content is stored once");
        }
        finally
        {
            await to.DisposeAsync();
        }

        string relative = (string)Scalar(destination, "SELECT relative_path FROM file_assets WHERE hash=$h;", ("$h", hash))!;
        CollectionAssert.AreEqual(content, File.ReadAllBytes(Path.Combine(destination, "files", relative)));
        Assert.AreEqual(Utc(1), Scalar(destination, "SELECT created_utc FROM day_files WHERE id=$id;", ("$id", NoteId(10))));
        Assert.AreEqual("report.csv", Scalar(destination, "SELECT display_name FROM day_files WHERE id=$id;", ("$id", NoteId(10))));
        Assert.AreEqual(2L, Count(destination, "SELECT COUNT(*) FROM sync_outbox WHERE entity='file' AND entity_id IN ($a,$b);", ("$a", NoteId(10)), ("$b", NoteId(11))));
        Assert.AreEqual(0L, Count(destination, "SELECT COUNT(*) FROM day_files WHERE id=$id;", ("$id", NoteId(12))));
    }

    [TestMethod]
    public async Task An_attachment_deleted_in_the_destination_after_it_was_added_stays_deleted()
    {
        using var root = new TempDirectory();
        string source = Path.Combine(root.Path, "src");
        string destination = Path.Combine(root.Path, "dst");

        SqliteDatabase from = Open(source);
        try
        {
            await InsertFileAsync(from, source, NoteId(10), "2026-09-01", "a.txt", "a"u8.ToArray(), Utc(1));
        }
        finally
        {
            await from.DisposeAsync();
        }

        SqliteDatabase to = Open(destination);
        try
        {
            await ExecAsync(
                to,
                "INSERT INTO sync_tombstones(entity,entity_id,deleted_utc) VALUES('file',$id,$d);",
                ("$id", NoteId(10)), ("$d", Utc(2)));

            ProfileImportResult result = await new ProfileImporter(to, destination).ImportFromAsync(source);

            Assert.AreEqual(0, result.FilesImported);
            Assert.AreEqual(1, result.FilesSkipped);
        }
        finally
        {
            await to.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task The_untouched_sample_is_neither_counted_nor_imported_but_an_edited_one_is()
    {
        using var root = new TempDirectory();
        string source = Path.Combine(root.Path, "src");
        string destination = Path.Combine(root.Path, "dst");
        string sample = NoteId(7);

        SqliteDatabase from = Open(source);
        try
        {
            Assert.IsTrue(ProfileImporter.CountUserContent(from).IsEmpty);

            await InsertNoteAsync(from, sample, "2026-09-01", "Today's work notes (sample)", "sample body", 0, Utc(1), Utc(1), customTitle: true);
            await SetSettingAsync(from, OnboardingSettings.SampleNoteIdKey, sample);
            await SetSettingAsync(from, OnboardingSettings.SampleNoteBodyKey, "sample body");
            Assert.AreEqual(new ProfileContent(0, 0), ProfileImporter.CountUserContent(from), "untouched sample");

            await InsertNoteAsync(from, NoteId(8), "2026-09-02", "노트 1", "mine", 0, Utc(2), Utc(2));
            await InsertFileAsync(from, source, NoteId(9), "2026-09-02", "a.txt", "a"u8.ToArray(), Utc(2));
            Assert.AreEqual(new ProfileContent(1, 1), ProfileImporter.CountUserContent(from));
        }
        finally
        {
            await from.DisposeAsync();
        }

        SqliteDatabase to = Open(destination);
        try
        {
            ProfileImportResult result = await new ProfileImporter(to, destination).ImportFromAsync(source);
            Assert.AreEqual(1, result.NotesImported);
        }
        finally
        {
            await to.DisposeAsync();
        }

        Assert.AreEqual(0L, Count(destination, "SELECT COUNT(*) FROM notes WHERE id=$id;", ("$id", sample)));

        from = Open(source);
        try
        {
            await ExecAsync(from, "UPDATE notes SET body='sample body, edited' WHERE id=$id;", ("$id", sample));
            Assert.AreEqual(new ProfileContent(2, 1), ProfileImporter.CountUserContent(from), "an edited sample is the user's");
        }
        finally
        {
            await from.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task A_profile_cannot_import_itself_and_a_missing_source_imports_nothing()
    {
        using var root = new TempDirectory();
        SqliteDatabase to = Open(root.Path);
        try
        {
            var importer = new ProfileImporter(to, root.Path);
            await Assert.ThrowsAsync<ArgumentException>(async () => await importer.ImportFromAsync(root.Path));
            Assert.AreEqual(
                ProfileImportResult.Empty,
                await importer.ImportFromAsync(Path.Combine(root.Path, "nowhere")));
        }
        finally
        {
            await to.DisposeAsync();
        }
    }
}
