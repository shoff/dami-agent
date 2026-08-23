using Npgsql;
using Xunit;

namespace Dami.Persistence.Tests;

/// <summary>A live database, with the event-store tables built in a throwaway schema.</summary>
/// <remarks>
/// The schema is created from <c>tools/ddl/002_event_store.sql</c> rather than from a
/// copy, so these tests exercise the DDL that is actually deployed. A copy would drift,
/// and the drift would only show up in production.
/// </remarks>
public sealed class DatabaseFixture : IAsyncLifetime
{
    /// <summary>The schema the tests build in. Never <c>dami</c>.</summary>
    public const string SCHEMA = "dami_test";

    private const string CONNECTION =
        "Host=127.0.0.1;Port=5432;Database=dami-data;Username=dami_ddl;Passfile=/home/steve/.pgpass";

    /// <summary>Data source for the tests. Valid only between initialise and dispose.</summary>
    public NpgsqlDataSource DataSource { get; private set; } = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        this.DataSource = NpgsqlDataSource.Create(CONNECTION);
        await this.ExecuteAsync(TestDdl.EventStoreForSchema(SCHEMA)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await this.ExecuteAsync(TestDdl.DropEventStore(SCHEMA)).ConfigureAwait(false);
        await this.DataSource.DisposeAsync().ConfigureAwait(false);
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
