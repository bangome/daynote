using System.Collections.Concurrent;
using Daynote.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Tests.Persistence;

[TestClass]
public sealed class SerializedWriterTests
{
    [TestMethod]
    public async Task Test_SerializedWriter_when_operations_are_concurrent_completes_in_enqueue_order()
    {
        // Given
        await using var fixture = TestDatabase.Create(writerCapacity: 8);
        fixture.Database.Initialize();
        var completionOrder = new ConcurrentQueue<int>();

        // When
        var tasks = Enumerable.Range(0, 50)
            .Select(index => fixture.Database.WriteAsync(
                (connection, transaction, _) =>
                {
                    InsertSetting(connection, transaction, index);
                    completionOrder.Enqueue(index);
                    return index;
                }).AsTask())
            .ToArray();
        await Task.WhenAll(tasks);

        // Then
        CollectionAssert.AreEqual(Enumerable.Range(0, 50).ToArray(), completionOrder.ToArray());
    }

    [TestMethod]
    public async Task Test_SerializedWriter_when_capacity_is_reached_applies_backpressure()
    {
        // Given
        await using var fixture = TestDatabase.Create(writerCapacity: 1);
        fixture.Database.Initialize();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var first = fixture.Database.WriteAsync(
            (_, _, _) =>
            {
                entered.SetResult();
                release.Wait(TimeSpan.FromSeconds(10));
                return 1;
            }).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = fixture.Database.WriteAsync(static (_, _, _) => 2).AsTask();

        // When
        var third = fixture.Database.WriteAsync(static (_, _, _) => 3).AsTask();

        // Then
        Assert.IsFalse(third.IsCompleted);
        release.Set();
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, await Task.WhenAll(first, second, third));
    }

    [TestMethod]
    public async Task Test_SerializedWriter_when_operation_throws_rolls_back_and_propagates()
    {
        // Given
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();

        // When
        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => fixture.Database.WriteAsync<int>(
                (connection, transaction, _) =>
                {
                    InsertSetting(connection, transaction, 1);
                    throw new InvalidOperationException("sentinel failure");
                }).AsTask());

        // Then
        Assert.AreEqual("sentinel failure", exception.Message);
        using var connection = fixture.Database.OpenReadConnection();
        Assert.AreEqual(0L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM settings;"));
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task Test_SerializedWriter_when_rollback_fails_completes_current_and_queued_work_with_terminal_failure()
    {
        // Given
        var fixture = TestDatabase.Create(writerCapacity: 4);
        fixture.Database.Initialize();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var current = fixture.Database.WriteAsync<int>(
            (_, transaction, _) =>
            {
                entered.SetResult();
                release.Wait(TimeSpan.FromSeconds(5));
                transaction.Rollback();
                throw new InvalidOperationException("operation failure");
            }).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var queued = fixture.Database.WriteAsync(static (_, _, _) => 2).AsTask();

        try
        {
            // When
            release.Set();
            var currentFailure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => current.WaitAsync(TimeSpan.FromSeconds(2)));

            // Then
            var queuedFailure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => queued.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.AreEqual(currentFailure.Message, queuedFailure.Message);
            var disposeFailure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => fixture.Database.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.AreEqual(currentFailure.Message, disposeFailure.Message);
        }
        finally
        {
            release.Set();
            var directory = Path.GetDirectoryName(fixture.DatabasePath)!;
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task Test_SerializedWriter_when_queued_call_is_cancelled_completes_without_deadlock()
    {
        // Given
        await using var fixture = TestDatabase.Create(writerCapacity: 1);
        fixture.Database.Initialize();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var first = fixture.Database.WriteAsync(
            (_, _, _) =>
            {
                entered.SetResult();
                release.Wait(TimeSpan.FromSeconds(10));
                return 1;
            }).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var cancelled = fixture.Database.WriteAsync(static (_, _, _) => 2, cancellation.Token).AsTask();

        // When
        cancellation.Cancel();

        // Then
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => cancelled.WaitAsync(TimeSpan.FromSeconds(2)));
        release.Set();
        Assert.AreEqual(1, await first);
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task Test_SerializedWriter_when_token_is_already_cancelled_disposes_within_timeout()
    {
        // Given
        var fixture = TestDatabase.Create(writerCapacity: 1);
        fixture.Database.Initialize();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // When
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => fixture.Database.WriteAsync(static (_, _, _) => 1, cancellation.Token).AsTask());

        // Then
        await fixture.Database.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        await fixture.DisposeAsync();
    }

    [TestMethod]
    public async Task Test_SerializedWriter_when_disposed_with_queued_work_drains_and_rejects_new_work()
    {
        // Given
        var fixture = TestDatabase.Create(writerCapacity: 4);
        fixture.Database.Initialize();
        var values = new ConcurrentQueue<int>();
        var queued = Enumerable.Range(0, 20)
            .Select(index => fixture.Database.WriteAsync(
                (_, _, _) =>
                {
                    values.Enqueue(index);
                    return index;
                }).AsTask())
            .ToArray();

        // When
        await fixture.Database.DisposeAsync();

        // Then
        CollectionAssert.AreEqual(Enumerable.Range(0, 20).ToArray(), await Task.WhenAll(queued));
        CollectionAssert.AreEqual(Enumerable.Range(0, 20).ToArray(), values.ToArray());
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            () => fixture.Database.WriteAsync(static (_, _, _) => 21).AsTask());
        await fixture.DisposeAsync();
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task Test_SerializedWriter_when_pump_connection_fails_releases_all_waiting_producers()
    {
        // Given
        var directory = Path.Combine(Path.GetTempPath(), "daynote-task3-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var factory = new SqliteConnectionFactory(new SqliteDatabaseOptions(directory, 1));
        var writer = new SerializedWriter(factory, 1);
        var writes = Enumerable.Range(0, 20)
            .Select(_ => writer.ExecuteAsync(static (_, _, _) => 1).AsTask())
            .ToArray();

        try
        {
            // When
            var exception = await Assert.ThrowsExactlyAsync<SqliteException>(
                () => Task.WhenAll(writes).WaitAsync(TimeSpan.FromSeconds(3)));

            // Then
            Assert.AreEqual(14, exception.SqliteErrorCode);
            Assert.IsTrue(writes.All(static write => write.IsCompleted));
            await Assert.ThrowsExactlyAsync<SqliteException>(
                () => writer.ExecuteAsync(static (_, _, _) => 2).AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
            await Assert.ThrowsExactlyAsync<SqliteException>(() => writer.DisposeAsync().AsTask());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [Timeout(60000)]
    public async Task Test_SerializedWriter_when_1000_writes_are_concurrent_has_no_busy_loss_fk_or_fts_drift()
    {
        // Given
        await using var fixture = TestDatabase.Create(writerCapacity: 64);
        fixture.Database.Initialize();

        // When
        var writes = Enumerable.Range(0, 1000)
            .Select(index => fixture.Database.WriteAsync(
                (connection, transaction, _) => InsertStressRows(connection, transaction, index)).AsTask())
            .ToArray();
        await Task.WhenAll(writes).WaitAsync(TimeSpan.FromSeconds(45));

        // Then
        using var connection = fixture.Database.OpenReadConnection();
        Assert.AreEqual(1000L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM notes;"));
        Assert.AreEqual(1000L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM clipboard_items;"));
        Assert.AreEqual(2000L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM search_documents;"));
        Assert.AreEqual(2000L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM search_fts WHERE search_fts MATCH 'STRESS';"));
        var integrity = fixture.Database.CheckIntegrity();
        Assert.AreEqual(0, integrity.ForeignKeyViolationCount);
        Assert.AreEqual(integrity.SourceDocumentCount, integrity.FtsDocumentCount);
        Assert.IsTrue(integrity.IsValid);
    }

    private static void InsertSetting(SqliteConnection connection, SqliteTransaction transaction, int index)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO settings(key,value,updated_utc) VALUES ($key,$value,$utc);";
        command.Parameters.AddWithValue("$key", $"key-{index:D4}");
        command.Parameters.AddWithValue("$value", $"value-{index:D4}");
        command.Parameters.AddWithValue("$utc", "2026-07-15T00:00:00Z");
        command.ExecuteNonQuery();
    }

    private static int InsertStressRows(SqliteConnection connection, SqliteTransaction transaction, int index)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO notes(id,local_date,title,body,sort_order,revision,created_utc,updated_utc)
            VALUES ($noteId,'2026-07-15',$title,$body,$order,1,'2026-07-15T00:00:00Z','2026-07-15T00:00:00Z');
            INSERT INTO clipboard_items(id,local_date,captured_utc,sequence_number,kind,text_value,asset_hash,payload_hash,byte_length)
            VALUES ($clipId,'2026-07-15','2026-07-15T00:00:00Z',$sequence,'text',$body,NULL,$hash,12);
            INSERT INTO search_documents(source_type,source_id,local_date,title,body,title_folded,body_folded)
            VALUES ('note',$noteId,'2026-07-15',$title,$body,$title,$body),
                   ('clipboard',$clipId,'2026-07-15','',$body,'',$body);
            """;
        command.Parameters.AddWithValue("$noteId", $"note-{index:D4}");
        command.Parameters.AddWithValue("$clipId", $"clip-{index:D4}");
        command.Parameters.AddWithValue("$title", $"STRESS TITLE {index:D4}");
        command.Parameters.AddWithValue("$body", $"STRESS BODY {index:D4}");
        command.Parameters.AddWithValue("$order", index);
        command.Parameters.AddWithValue("$sequence", index);
        command.Parameters.AddWithValue("$hash", $"hash-{index:D4}");
        return command.ExecuteNonQuery();
    }
}
