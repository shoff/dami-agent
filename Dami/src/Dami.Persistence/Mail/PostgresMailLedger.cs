using Dami.Contracts.Mail;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Mail;

/// <summary>PostgreSQL store for filed forwarded mail (migration 046).</summary>
public sealed class PostgresMailLedger : IMailLedger
{
    private readonly NpgsqlDataSource dataSource;
    private readonly string schema;

    /// <summary>Creates the ledger.</summary>
    public PostgresMailLedger(NpgsqlDataSource dataSource, IOptions<PostgresOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        this.dataSource = dataSource;
        this.schema = options.Value.SchemaName;
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> FiledAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messageIds);
        var filed = new HashSet<string>(StringComparer.Ordinal);
        if (messageIds.Count == 0)
        {
            return filed;
        }

        await using var command = this.dataSource.CreateCommand(
            $"select message_id from {this.schema}.mail_items where message_id = any(@ids);");
        command.Parameters.AddWithValue("ids", messageIds.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            filed.Add(reader.GetString(0));
        }

        return filed;
    }

    /// <inheritdoc />
    public async Task<bool> RecordAsync(
        string messageId, DateTimeOffset receivedAt, string kind, string summary, DateTimeOffset filedAt,
        CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"insert into {this.schema}.mail_items (message_id, received_at, kind, summary, filed_at) "
            + "values (@id, @received, @kind, @summary, @filed) on conflict (message_id) do nothing;");
        command.Parameters.AddWithValue("id", messageId);
        command.Parameters.AddWithValue("received", receivedAt.ToUniversalTime());
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("summary", summary);
        command.Parameters.AddWithValue("filed", filedAt.ToUniversalTime());
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }
}
