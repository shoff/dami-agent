using System.Text.Json;
using System.Text.Json.Serialization;
using Dami.Contracts.Events;
using Dami.Contracts.ToolStaging;
using Dami.Persistence.Events;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Dami.Persistence.ToolStaging;

/// <summary>Immutable inert tool proposals in PostgreSQL.</summary>
public sealed class PostgresToolProposalStore : IToolProposalStore
{
    private static readonly JsonSerializerOptions serializerOptions = CreateSerializerOptions();

    private readonly NpgsqlDataSource dataSource;
    private readonly string eventsTable;
    private readonly string table;

    /// <summary>Creates the tool proposal store.</summary>
    public PostgresToolProposalStore(
        NpgsqlDataSource dataSource,
        IOptions<PostgresOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        this.dataSource = dataSource;
        string schema = options.Value.SchemaName;
        this.eventsTable = $"{schema}.execution_events";
        this.table = $"{schema}.tool_proposals";
    }

    /// <inheritdoc />
    public async Task<StagedToolProposal> StageAsync(
        StagedToolProposal proposal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        await using var connection = await this.dataSource
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await this.InsertAsync(connection, transaction, proposal, cancellationToken)
            .ConfigureAwait(false);
        StagedToolProposal accepted = await this.FindRequiredAsync(
            connection, transaction, proposal.Request.ProposalId, cancellationToken)
            .ConfigureAwait(false);
        EnsureExactRetry(proposal, accepted);
        await ExecutionEventCommand.AppendExactAsync(
            connection, transaction, this.eventsTable,
            ToolProposalEventFactory.Proposed(accepted), cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return accepted;
    }

    /// <inheritdoc />
    public async Task<StagedToolProposal?> FindAsync(
        Guid proposalId,
        CancellationToken cancellationToken)
    {
        ValidateProposalId(proposalId);
        await using var connection = await this.dataSource
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await this.FindCoreAsync(
            connection, null, proposalId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ToolProposalSummary>> ListAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit is <= 0 or > ToolProposalReviewLimits.MAX_LIST_LIMIT)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit), limit,
                $"Proposal list limits must be 1–{ToolProposalReviewLimits.MAX_LIST_LIMIT}.");
        }

        await using var command = this.dataSource.CreateCommand($"""
            select proposal_id, capability_id, artifact -> 'Schema' ->> 'Name',
                   artifact_version, artifact ->> 'ExecutionProfile', origin, proposed_at
              from {this.table}
             order by proposed_at desc, proposal_id desc
             limit @limit;
            """);
        command.Parameters.AddWithValue("limit", limit);
        var proposals = new List<ToolProposalSummary>();
        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            proposals.Add(new ToolProposalSummary(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                Enum.Parse<ToolExecutionProfile>(reader.GetString(4)),
                Enum.Parse<ExecutionOrigin>(reader.GetString(5)),
                await reader.GetFieldValueAsync<DateTimeOffset>(6, cancellationToken)
                    .ConfigureAwait(false)));
        }

        return proposals;
    }

    private async Task InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StagedToolProposal proposal,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            insert into {this.table}
                (proposal_id, trace_id, span_id, parent_span_id, origin,
                 capability_id, artifact_version, artifact, proposed_at)
            values
                (@proposal, @trace, @span, @parent, @origin,
                 @capability, @version, @artifact, @at)
            on conflict (proposal_id) do nothing;
            """,
            connection,
            transaction);
        AddParameters(command, proposal);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<StagedToolProposal> FindRequiredAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid proposalId,
        CancellationToken cancellationToken)
    {
        return await this.FindCoreAsync(
            connection, transaction, proposalId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The staged tool proposal could not be reloaded.");
    }

    private async Task<StagedToolProposal?> FindCoreAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid proposalId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            select proposal_id, trace_id, span_id, parent_span_id, origin,
                   artifact_version, artifact::text, proposed_at
              from {this.table}
             where proposal_id = @proposal;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("proposal", proposalId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? await ReadAsync(reader, cancellationToken).ConfigureAwait(false)
            : null;
    }

    private static async Task<StagedToolProposal> ReadAsync(
        NpgsqlDataReader reader,
        CancellationToken cancellationToken)
    {
        bool parentNull = await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false);
        string json = reader.GetString(6);
        ToolProposalArtifact artifact = JsonSerializer.Deserialize<ToolProposalArtifact>(
            json, serializerOptions)
            ?? throw new InvalidDataException("Stored tool proposal artifact is null.");
        var request = new ToolProposalRequest(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
            parentNull ? null : reader.GetGuid(3),
            Enum.Parse<ExecutionOrigin>(reader.GetString(4)), artifact);
        return new StagedToolProposal(
            request, reader.GetString(5),
            await reader.GetFieldValueAsync<DateTimeOffset>(7, cancellationToken).ConfigureAwait(false));
    }

    private static void AddParameters(NpgsqlCommand command, StagedToolProposal proposal)
    {
        ToolProposalRequest request = proposal.Request;
        command.Parameters.AddWithValue("proposal", request.ProposalId);
        command.Parameters.AddWithValue("trace", request.TraceId);
        command.Parameters.AddWithValue("span", request.SpanId);
        command.Parameters.AddWithValue("parent", (object?)request.ParentSpanId ?? DBNull.Value);
        command.Parameters.AddWithValue("origin", request.Origin.ToString());
        command.Parameters.AddWithValue("capability", request.Artifact.Schema.CapabilityId);
        command.Parameters.AddWithValue("version", proposal.ArtifactVersion);
        command.Parameters.Add(new NpgsqlParameter("artifact", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(request.Artifact, serializerOptions),
        });
        command.Parameters.AddWithValue("at", proposal.ProposedAt);
    }

    private static void EnsureExactRetry(
        StagedToolProposal attempted,
        StagedToolProposal accepted)
    {
        ToolProposalRequest left = attempted.Request;
        ToolProposalRequest right = accepted.Request;
        bool matches = left.TraceId == right.TraceId
            && left.SpanId == right.SpanId
            && left.ParentSpanId == right.ParentSpanId
            && left.Origin == right.Origin
            && left.Artifact.Schema.CapabilityId == right.Artifact.Schema.CapabilityId
            && string.Equals(
                attempted.ArtifactVersion, accepted.ArtifactVersion, StringComparison.Ordinal)
            && ArtifactsEqual(left.Artifact, right.Artifact);
        if (!matches)
        {
            throw new InvalidOperationException(
                $"Tool proposal '{left.ProposalId}' conflicts with its immutable stored value.");
        }
    }

    private static bool ArtifactsEqual(
        ToolProposalArtifact left,
        ToolProposalArtifact right)
    {
        return string.Equals(left.Schema.Name, right.Schema.Name, StringComparison.Ordinal)
            && string.Equals(left.Schema.Description, right.Schema.Description, StringComparison.Ordinal)
            && JsonElement.DeepEquals(left.Schema.Parameters, right.Schema.Parameters)
            && left.Tags.SequenceEqual(right.Tags, StringComparer.Ordinal)
            && FilesEqual(left.SourceFiles, right.SourceFiles)
            && FilesEqual(left.TestFiles, right.TestFiles)
            && string.Equals(left.Rationale, right.Rationale, StringComparison.Ordinal)
            && left.ObservationIds.SequenceEqual(right.ObservationIds)
            && left.ExecutionProfile == right.ExecutionProfile;
    }

    private static bool FilesEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (KeyValuePair<string, string> file in left)
        {
            if (!right.TryGetValue(file.Key, out string? content)
                || !string.Equals(file.Value, content, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateProposalId(Guid proposalId)
    {
        if (proposalId == Guid.Empty)
        {
            throw new ArgumentException("A proposal identifier cannot be empty.", nameof(proposalId));
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
