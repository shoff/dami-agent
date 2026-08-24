using Npgsql;
using Xunit;

namespace Dami.Persistence.Tests;

/// <summary>A live database, with the event-store tables built in a throwaway schema.</summary>
/// <remarks>
/// The schema is created from <c>tools/ddl/002_event_store.sql</c> rather than from a
/// copy, so these tests exercise the DDL that is actually deployed. A copy would drift,
/// and the drift would only show up in production.
///
/// Concurrent test runs serialize on a Postgres advisory lock (N6). Two runs share one
/// <c>dami_test</c> schema — <c>dami_ddl</c> deliberately holds no CREATE privilege on
/// the database, so a per-run schema is not available — and without the lock the second
/// run's setup drops the first run's tables mid-flight, producing a cascade of failures
/// that look like real defects and vanish on re-run. The lock is session-scoped, so a
/// crashed run releases it automatically rather than wedging the next one.
/// </remarks>
public sealed class DatabaseFixture : IAsyncLifetime
{
    /// <summary>The schema the tests build in. Never <c>dami</c>.</summary>
    public const string SCHEMA = "dami_test";

    private const string CONNECTION =
        "Host=127.0.0.1;Port=5432;Database=dami-data;Username=dami_ddl;Passfile=/home/steve/.pgpass";

    private const string RUNTIME_CONNECTION =
        "Host=127.0.0.1;Port=5432;Database=dami-data;Username=dami_app;Passfile=/home/steve/.pgpass";

    /// <summary>Advisory-lock key for the shared test schema. Arbitrary but stable.</summary>
    private const long SCHEMA_LOCK = 0x44414D49_54455354;

    private NpgsqlConnection? schemaLock;

    /// <summary>Data source for the tests. Valid only between initialise and dispose.</summary>
    public NpgsqlDataSource DataSource { get; private set; } = null!;

    /// <summary>Creates a runtime-role source for least-privilege integration tests.</summary>
    public static NpgsqlDataSource CreateRuntimeDataSource()
    {
        return NpgsqlDataSource.Create(RUNTIME_CONNECTION);
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        this.DataSource = NpgsqlDataSource.Create(CONNECTION);

        // Held on a dedicated connection for the fixture's lifetime: a pooled connection
        // would return to the pool still holding the lock, and the release would have to
        // find its way back to that same physical session.
        this.schemaLock = new NpgsqlConnection(CONNECTION);
        await this.schemaLock.OpenAsync().ConfigureAwait(false);
        await using (var acquire = new NpgsqlCommand($"select pg_advisory_lock({SCHEMA_LOCK});", this.schemaLock))
        {
            await acquire.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await this.ExecuteAsync(TestDdl.EventStoreForSchema(SCHEMA)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await this.ExecuteAsync(TestDdl.DropEventStore(SCHEMA)).ConfigureAwait(false);
        await this.DataSource.DisposeAsync().ConfigureAwait(false);

        if (this.schemaLock is not null)
        {
            // Closing the session releases the advisory lock; the explicit unlock keeps
            // the intent visible rather than relying on that.
            await using (var release = new NpgsqlCommand($"select pg_advisory_unlock({SCHEMA_LOCK});", this.schemaLock))
            {
                await release.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            await this.schemaLock.DisposeAsync().ConfigureAwait(false);
            this.schemaLock = null;
        }
    }

    /// <summary>Empties the table between tests, bypassing the append-only trigger deliberately.</summary>
    public async Task ResetAsync()
    {
        await this.ExecuteAsync(TestDdl.TruncateEventStore(SCHEMA)).ConfigureAwait(false);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var command = this.DataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}

/// <summary>Marks the tests that share one database fixture.</summary>
[CollectionDefinition(NAME)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    /// <summary>The collection name.</summary>
    public const string NAME = "database";
}
