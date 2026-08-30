using Dami.Contracts.Proactive;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Proactive;

/// <summary>What the database says it has applied.</summary>
/// <remarks>
/// Deliberately the database's own answer rather than a scan of <c>tools/ddl</c>. The
/// hygiene watcher compares this against what git tracks, and that comparison is only
/// worth anything if the two sides are independent: asking the filesystem for both would
/// always agree with itself.
///
/// The ledger lives outside the schema-qualified store options because it is infrastructure
/// about the schema rather than data within it; it is read from whatever schema the
/// migrations built.
/// </remarks>
public sealed class PostgresSchemaLedger : ISchemaLedger
{
    private readonly NpgsqlDataSource dataSource;
    private readonly PostgresOptions storeOptions;

    /// <summary>Creates the ledger reader.</summary>
    public PostgresSchemaLedger(NpgsqlDataSource dataSource, IOptions<PostgresOptions> storeOptions)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(storeOptions);
        this.dataSource = dataSource;
        this.storeOptions = storeOptions.Value;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> AppliedAsync(CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"select filename from {this.storeOptions.SchemaName}.schema_migrations order by filename;");

        var applied = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            applied.Add(reader.GetString(0));
        }

        return applied;
    }
}
