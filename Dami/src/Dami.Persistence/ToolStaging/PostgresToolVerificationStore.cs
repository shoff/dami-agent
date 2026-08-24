using Dami.Contracts.Events;
using Dami.Contracts.ToolStaging;
using Dami.Persistence.Events;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.ToolStaging;

/// <summary>Append-only exact-artifact verification evidence and events.</summary>
public sealed class PostgresToolVerificationStore : IToolVerificationStore
{
    private readonly NpgsqlDataSource dataSource;
    private readonly string eventsTable;
    private readonly string proposalsTable;
    private readonly string table;

    /// <summary>Creates the PostgreSQL verification store.</summary>
    public PostgresToolVerificationStore(
        NpgsqlDataSource dataSource,
        IOptions<PostgresOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        this.dataSource = dataSource;
        string schema = options.Value.SchemaName;
        this.eventsTable = $"{schema}.execution_events";
        this.proposalsTable = $"{schema}.tool_proposals";
        this.table = $"{schema}.tool_verifications";
    }

    /// <inheritdoc />
    public async Task<ToolVerificationRecord> RecordAsync(
        ToolVerificationRecord record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        await using var connection = await this.dataSource
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await this.InsertAsync(connection, transaction, record, cancellationToken)
            .ConfigureAwait(false);
        ToolVerificationRecord accepted = await this.FindRequiredAsync(
            connection, transaction, record.ProposalId, record.ArtifactVersion,
            cancellationToken).ConfigureAwait(false);
        if (accepted != record)
        {
            throw new InvalidOperationException(
                $"Tool verification for proposal '{record.ProposalId}' conflicts with its stored value.");
        }

        (Guid traceId, Guid spanId, ExecutionOrigin origin) = await this.FindProvenanceAsync(
            connection, transaction, record.ProposalId, cancellationToken).ConfigureAwait(false);
        await ExecutionEventCommand.AppendExactAsync(
            connection,
            transaction,
            this.eventsTable,
            ToolVerificationEventFactory.Verified(accepted, traceId, spanId, origin),
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return accepted;
    }

    /// <inheritdoc />
    public async Task<ToolVerificationRecord?> FindAsync(
        Guid proposalId,
        string artifactVersion,
        CancellationToken cancellationToken)
    {
        ValidateKey(proposalId, artifactVersion);
        await using var connection = await this.dataSource
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await this.FindCoreAsync(
            connection, null, proposalId, artifactVersion, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ToolVerificationRecord record,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"""
            insert into {this.table}
                (verification_id, proposal_id, artifact_version, assembly_sha256,
                 test_evidence, verified_at)
            values (@verification, @proposal, @version, @assembly, @evidence, @at)
            on conflict do nothing;
            """, connection, transaction);
        AddParameters(command, record);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ToolVerificationRecord> FindRequiredAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid proposalId,
        string artifactVersion,
        CancellationToken cancellationToken)
    {
        return await this.FindCoreAsync(
            connection, transaction, proposalId, artifactVersion, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The tool verification could not be reloaded.");
    }

    private async Task<ToolVerificationRecord?> FindCoreAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid proposalId,
        string artifactVersion,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"""
            select verification_id, proposal_id, artifact_version, assembly_sha256,
                   test_evidence, verified_at
              from {this.table}
             where proposal_id = @proposal and artifact_version = @version;
            """, connection, transaction);
        command.Parameters.AddWithValue("proposal", proposalId);
        command.Parameters.AddWithValue("version", artifactVersion);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new ToolVerificationRecord(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4),
                await reader.GetFieldValueAsync<DateTimeOffset>(5, cancellationToken)
                    .ConfigureAwait(false))
            : null;
    }

    private async Task<(Guid TraceId, Guid SpanId, ExecutionOrigin Origin)> FindProvenanceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid proposalId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"""
            select trace_id, span_id, origin
              from {this.proposalsTable}
             where proposal_id = @proposal;
            """, connection, transaction);
        command.Parameters.AddWithValue("proposal", proposalId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The verified proposal could not be reloaded.");
        }

        return (reader.GetGuid(0), reader.GetGuid(1), Enum.Parse<ExecutionOrigin>(reader.GetString(2)));
    }

    private static void AddParameters(NpgsqlCommand command, ToolVerificationRecord record)
    {
        command.Parameters.AddWithValue("verification", record.VerificationId);
        command.Parameters.AddWithValue("proposal", record.ProposalId);
        command.Parameters.AddWithValue("version", record.ArtifactVersion);
        command.Parameters.AddWithValue("assembly", record.AssemblySha256);
        command.Parameters.AddWithValue("evidence", record.TestEvidence);
        command.Parameters.AddWithValue("at", record.VerifiedAt);
    }

    private static void ValidateKey(Guid proposalId, string artifactVersion)
    {
        if (proposalId == Guid.Empty)
        {
            throw new ArgumentException("A proposal identifier cannot be empty.", nameof(proposalId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(artifactVersion);
    }
}
