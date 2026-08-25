using Dami.Contracts.Briefs;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Briefs;

/// <summary>Egress briefs in Postgres, one row per consent request.</summary>
public sealed class PostgresEgressBriefStore : IEgressBriefStore
{
    private readonly NpgsqlDataSource dataSource;
    private readonly PostgresOptions storeOptions;

    /// <summary>Creates the store.</summary>
    public PostgresEgressBriefStore(NpgsqlDataSource dataSource, IOptions<PostgresOptions> storeOptions)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(storeOptions);

        this.dataSource = dataSource;
        this.storeOptions = storeOptions.Value;
    }

    private string Table => $"{this.storeOptions.SchemaName}.egress_briefs";

    /// <inheritdoc />
    public async Task CreateAsync(EgressBrief brief, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(brief);

        await using var command = this.dataSource.CreateCommand(
            $"""
            insert into {this.Table}
                (brief_id, approval_id, trace_id, question, brief, brief_sha256, created_at)
            values (@brief_id, @approval_id, @trace_id, @question, @brief, @sha, @created_at);
            """);
        command.Parameters.AddWithValue("brief_id", brief.BriefId);
        command.Parameters.AddWithValue("approval_id", (object?)brief.ApprovalId ?? DBNull.Value);
        command.Parameters.AddWithValue("trace_id", brief.TraceId);
        command.Parameters.AddWithValue("question", brief.Question);
        command.Parameters.AddWithValue("brief", brief.Brief);
        command.Parameters.AddWithValue("sha", brief.BriefSha256);
        command.Parameters.AddWithValue("created_at", brief.CreatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<EgressBrief?> FindByApprovalAsync(Guid approvalId, CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"""
            select brief_id, approval_id, trace_id, question, brief, brief_sha256,
                   created_at, sent_at, answer
              from {this.Table}
             where approval_id = @approval_id;
            """);
        command.Parameters.AddWithValue("approval_id", approvalId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var approvalNull = await reader.IsDBNullAsync(1, cancellationToken).ConfigureAwait(false);
        var sentAtNull = await reader.IsDBNullAsync(7, cancellationToken).ConfigureAwait(false);
        var answerNull = await reader.IsDBNullAsync(8, cancellationToken).ConfigureAwait(false);
        return new EgressBrief(
            briefId: reader.GetGuid(0),
            approvalId: approvalNull ? null : reader.GetGuid(1),
            traceId: reader.GetGuid(2),
            question: reader.GetString(3),
            brief: reader.GetString(4),
            briefSha256: reader.GetString(5),
            createdAt: await reader.GetFieldValueAsync<DateTimeOffset>(6, cancellationToken).ConfigureAwait(false),
            sentAt: sentAtNull
                ? null
                : await reader.GetFieldValueAsync<DateTimeOffset>(7, cancellationToken).ConfigureAwait(false),
            answer: answerNull ? null : reader.GetString(8));
    }

    /// <inheritdoc />
    public async Task MarkSentAsync(
        Guid briefId,
        string answer,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(answer);

        await using var command = this.dataSource.CreateCommand(
            $"update {this.Table} set sent_at = @sent_at, answer = @answer where brief_id = @brief_id;");
        command.Parameters.AddWithValue("brief_id", briefId);
        command.Parameters.AddWithValue("sent_at", sentAt);
        command.Parameters.AddWithValue("answer", answer);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
