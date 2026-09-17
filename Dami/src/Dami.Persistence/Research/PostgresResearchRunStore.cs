using System.Text.Json;
using Dami.Contracts.Events;
using Dami.Contracts.Research;
using Dami.Persistence.Events;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Dami.Persistence.Research;

/// <summary>Persists research artifacts in the configured PostgreSQL schema.</summary>
public sealed class PostgresResearchRunStore : IResearchRunStore
{
    private readonly NpgsqlDataSource dataSource;
    private readonly string table;
    private readonly string events;

    /// <summary>Creates a research archive using the runtime database role.</summary>
    public PostgresResearchRunStore(NpgsqlDataSource dataSource, IOptions<PostgresOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        this.dataSource = dataSource;
        this.table = $"\"{options.Value.SchemaName}\".research_runs";
        this.events = $"\"{options.Value.SchemaName}\".execution_events";
    }

    /// <inheritdoc />
    public async Task SaveAsync(ResearchRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        await using var connection = await this.dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = this.SaveCommand(connection, transaction, run);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0)
        {
            await ExecutionEventCommand.AppendAsync(connection, transaction, this.events, Event(run), cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private NpgsqlCommand SaveCommand(NpgsqlConnection connection, NpgsqlTransaction transaction, ResearchRun run)
    {
        var command = new NpgsqlCommand($"""
            insert into {this.table} as current (run_id, revision, trace_id, started_at, summary, snapshot)
            values (@id, @revision, @trace, @started, @summary, @snapshot)
            on conflict (run_id) do update set snapshot = excluded.snapshot,
                summary = excluded.summary, revision = excluded.revision
            where current.revision < excluded.revision;
            """, connection, transaction);
        command.Parameters.AddWithValue("id", run.RunId);
        command.Parameters.AddWithValue("revision", run.Revision);
        command.Parameters.AddWithValue("trace", run.TraceId);
        command.Parameters.AddWithValue("started", run.StartedAt);
        command.Parameters.AddWithValue("summary", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(run.Summarize()));
        command.Parameters.AddWithValue("snapshot", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(run));
        return command;
    }

    private static ExecutionEvent Event(ResearchRun run)
    {
        var (type, status) = run.Status switch
        {
            ResearchRunStatus.Completed => (ExecutionEventType.ToolCompleted, ExecutionStatus.Succeeded),
            ResearchRunStatus.Cancelled => (ExecutionEventType.ToolFailed, ExecutionStatus.Cancelled),
            ResearchRunStatus.Failed or ResearchRunStatus.Interrupted => (ExecutionEventType.ToolFailed, ExecutionStatus.Failed),
            _ => (run.Revision == 1 ? ExecutionEventType.ToolStarted : ExecutionEventType.AgentProgressed, ExecutionStatus.Running),
        };
        return new ExecutionEvent(Guid.NewGuid(), run.TraceId, run.RunId, null, run.Origin, "deep-research", type,
            status, run.UpdatedAt, $"Research {run.Status}: {run.Findings.Count} sources, {run.PagesAttempted} reads",
            $"research:{run.RunId:D}");
    }

    /// <inheritdoc />
    public async Task<ResearchRun?> GetAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand($"select snapshot from {this.table} where run_id = @id");
        command.Parameters.AddWithValue("id", runId);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is string json ? JsonSerializer.Deserialize<ResearchRun>(json) : null;
    }

    /// <inheritdoc />
    public async Task<ResearchRun?> FindByTraceAsync(Guid traceId, CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"select snapshot from {this.table} where trace_id = @id order by started_at desc limit 1");
        command.Parameters.AddWithValue("id", traceId);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is string json ? JsonSerializer.Deserialize<ResearchRun>(json) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResearchRunSummary>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"select summary from {this.table} order by started_at desc, run_id limit @limit");
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 100));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<ResearchRunSummary>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(JsonSerializer.Deserialize<ResearchRunSummary>(reader.GetString(0))!);
        }

        return results;
    }
}
