using Daynote.Core.Settings;
using Daynote.Core.Time;
using Daynote.Infrastructure.Persistence;
using Daynote.Infrastructure.Settings;
using Daynote.Infrastructure.Tests.Persistence;

namespace Daynote.Infrastructure.Tests.Settings;

[TestClass]
public sealed class SqliteSettingsStoreTests
{
    private sealed class FixedClock : IClock
    {
        public ClockSnapshot Read() => new(new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero), TimeSpan.Zero);
    }

    [TestMethod]
    public async Task Test_GetAsync_when_key_absent_returns_null()
    {
        await using TestDatabase db = TestDatabase.Create();
        db.Database.Initialize();
        var store = new SqliteSettingsStore(db.Database, new FixedClock());

        Assert.IsNull(await store.GetAsync("lifecycle.consent"));
        Assert.IsTrue(await store.GetBoolAsync("lifecycle.capture-paused", fallback: true));
        Assert.IsFalse(await store.GetBoolAsync("lifecycle.capture-paused", fallback: false));
    }

    [TestMethod]
    public async Task Test_SetAsync_round_trips_string_and_bool_values()
    {
        await using TestDatabase db = TestDatabase.Create();
        db.Database.Initialize();
        var store = new SqliteSettingsStore(db.Database, new FixedClock());

        await store.SetAsync(UiSettings.LanguageKey, "ko");
        await store.SetBoolAsync(OnboardingSettings.CompletedKey, true);

        Assert.AreEqual("ko", await store.GetAsync(UiSettings.LanguageKey));
        Assert.IsTrue(await store.GetBoolAsync(OnboardingSettings.CompletedKey, fallback: false));
    }

    [TestMethod]
    public async Task Test_SetAsync_upserts_existing_key_without_duplicating()
    {
        await using TestDatabase db = TestDatabase.Create();
        db.Database.Initialize();
        var store = new SqliteSettingsStore(db.Database, new FixedClock());

        await store.SetBoolAsync(OnboardingSettings.CompletedKey, true);
        await store.SetBoolAsync(OnboardingSettings.CompletedKey, false);

        Assert.IsFalse(await store.GetBoolAsync(OnboardingSettings.CompletedKey, fallback: true));
    }

    [TestMethod]
    public async Task Test_Values_persist_across_a_reopen_of_the_same_database()
    {
        var directory = Path.Combine(Path.GetTempPath(), "daynote-settings-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "daynote.db");
        try
        {
            var first = new SqliteDatabase(new SqliteDatabaseOptions(path));
            first.Initialize();
            var writeStore = new SqliteSettingsStore(first, new FixedClock());
            await writeStore.SetAsync(UiSettings.LanguageKey, "ko");
            await writeStore.SetBoolAsync(OnboardingSettings.CompletedKey, true);
            await first.DisposeAsync();

            var second = new SqliteDatabase(new SqliteDatabaseOptions(path));
            second.Initialize();
            var readStore = new SqliteSettingsStore(second, new FixedClock());

            Assert.AreEqual("ko", await readStore.GetAsync(UiSettings.LanguageKey));
            Assert.IsTrue(await readStore.GetBoolAsync(OnboardingSettings.CompletedKey, fallback: false));
            await second.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task Test_Lifecycle_keys_do_not_collide_with_note_custom_title_markers()
    {
        await using TestDatabase db = TestDatabase.Create();
        db.Database.Initialize();
        var store = new SqliteSettingsStore(db.Database, new FixedClock());

        // The note repository uses note.custom-title.* markers in the same table; a lifecycle write
        // must not read or clobber them.
        await store.SetAsync("note.custom-title.abc", "1");
        await store.SetBoolAsync(OnboardingSettings.CompletedKey, true);

        Assert.AreEqual("1", await store.GetAsync("note.custom-title.abc"));
        Assert.IsTrue(await store.GetBoolAsync(OnboardingSettings.CompletedKey, fallback: false));
    }
}
