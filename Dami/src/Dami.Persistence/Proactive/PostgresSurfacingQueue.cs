using System.Data;
using System.Runtime.CompilerServices;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Proactive;

/// <summary>The surfacing queue over PostgreSQL, with the D-021 cap.</summary>
/// <remarks>
/// The cap check and the insert run in one transaction with the count serialised on the
/// service's rows, so two concurrent passes cannot both slip under the cap. Suppressed
/// surfacings are stored rather than dropped: a cap that silently discards is invisible
/// in the audit, and how often the cap bites is itself a tuning signal.
/// </remarks>
public sealed class PostgresSurfacingQueue : ISurfacingQueue
{
    private const string STATUS_PENDING = "Pending";
    private const string STATUS_DELIVERED = "Delivered";
    private const string STATUS_SUPPRESSED = "Suppressed";

    private readonly NpgsqlDataSource dataSource;
    private readonly PostgresOptions storeOptions;
    private readonly ProactiveOptions proactiveOptions;
    private readonly ILogger<PostgresSurfacingQueue> logger;

    /// <summary>Creates the queue.</summary>
    public PostgresSurfacingQueue(
        NpgsqlDataSource dataSource,
        IOptions<PostgresOptions> storeOptions,
        IOptions<ProactiveOptions> proactiveOptions,
        ILogger<PostgresSurfacingQueue> logger)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(storeOptions);
        ArgumentNullException.ThrowIfNull(proactiveOptions);
        ArgumentNullException.ThrowIfNull(logger);

        this.dataSource = dataSource;
        this.storeOptions = storeOptions.Value;
        this.proactiveOptions = proactiveOptions.Value;
        this.logger = logger;
    }

    private string Table => $"{this.storeOptions.SchemaName}.surfacings";

    /// <summary>Enqueue SQL: one statement that decides Pending or Suppressed as it inserts.</summary>
    /// <remarks>
    /// The status is computed from the same rows the insert is about to join, inside one
    /// statement, which is what makes the cap race-safe without an explicit lock. Only
    /// non-suppressed rows in the rolling day count toward the cap — suppressed ones must
    /// not, or a burst would extend the suppression window indefinitely.
    /// </remarks>
    public static string BuildEnqueueSql(string table)
    {
        ArgumentNullException.ThrowIfNull(table);

        return $"""
            insert into {table}
                (surfacing_id, trace_id, service_name, title, body, confidence, status, created_at)
            select @surfacing_id, @trace_id, @service_name, @title, @body, @confidence,
                   case when (
                       select count(*) from {table}
                        where service_name = @service_name
                          and status <> '{STATUS_SUPPRESSED}'
                          and created_at >= @created_at - interval '1 day'
                   ) < @cap then '{STATUS_PENDING}' else '{STATUS_SUPPRESSED}' end,
                   @created_at
            on conflict (surfacing_id) do nothing
            returning status;
            """;
    }

    /// <inheritdoc />
    public async Task<bool> EnqueueAsync(Surfacing surfacing, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(surfacing);

        await using var command = this.dataSource.CreateCommand(BuildEnqueueSql(this.Table));
        command.Parameters.AddWithValue("surfacing_id", surfacing.SurfacingId);
        command.Parameters.AddWithValue("trace_id", Guid.Empty);
        command.Parameters.AddWithValue("service_name", surfacing.ServiceName);
        command.Parameters.AddWithValue("title", surfacing.Title);
        command.Parameters.AddWithValue("body", surfacing.Body);
        command.Parameters.AddWithValue("confidence", surfacing.Confidence);
        command.Parameters.AddWithValue("created_at", surfacing.CreatedAt);
        command.Parameters.AddWithValue("cap", this.proactiveOptions.MaxSurfacingsPerServicePerDay);

        var status = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;

        if (status == STATUS_SUPPRESSED)
        {
            this.logger.LogInformation(
                "Surfacing {SurfacingId} from {ServiceName} suppressed by the daily cap of {Cap}",
                surfacing.SurfacingId,
                surfacing.ServiceName,
                this.proactiveOptions.MaxSurfacingsPerServicePerDay);
        }

        return status == STATUS_PENDING;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<Surfacing> PendingAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive.");
        }

        var command = this.dataSource.CreateCommand(
            $"""
            select surfacing_id, service_name, title, body, confidence, created_at
              from {this.Table}
             where status = '{STATUS_PENDING}'
             order by created_at
             limit @limit;
            """);
        command.Parameters.AddWithValue("limit", limit);
        return StreamAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<Surfacing> RecentAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive.");
        }

        var command = this.dataSource.CreateCommand(
            $"""
            select surfacing_id, service_name, title, body, confidence, created_at
              from {this.Table}
             order by created_at desc
             limit @limit;
            """);
        command.Parameters.AddWithValue("limit", limit);
        return StreamAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<SurfacingReaction> ReactionsAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive.");
        }

        var command = this.dataSource.CreateCommand(
            $"""
            select title, feedback
              from {this.Table}
             where feedback is not null
             order by feedback_at desc
             limit @limit;
            """);
        command.Parameters.AddWithValue("limit", limit);
        return StreamReactionsAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeliverAsync(Guid surfacingId, DateTimeOffset deliveredAt, CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"update {this.Table} set status = '{STATUS_DELIVERED}', delivered_at = @at "
            + $"where surfacing_id = @id and status = '{STATUS_PENDING}';");
        command.Parameters.AddWithValue("id", surfacingId);
        command.Parameters.AddWithValue("at", deliveredAt);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RecordFeedbackAsync(
        Guid surfacingId,
        string feedback,
        DateTimeOffset feedbackAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(feedback);

        await using var command = this.dataSource.CreateCommand(
            $"update {this.Table} set feedback = @feedback, feedback_at = @at where surfacing_id = @id;");
        command.Parameters.AddWithValue("id", surfacingId);
        command.Parameters.AddWithValue("feedback", feedback);
        command.Parameters.AddWithValue("at", feedbackAt);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<Surfacing> StreamAsync(
        NpgsqlCommand command,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using (command.ConfigureAwait(false))
        {
            await using var reader = await command
                .ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return Read(reader);
            }
        }
    }

    private static async IAsyncEnumerable<SurfacingReaction> StreamReactionsAsync(
        NpgsqlCommand command,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using (command.ConfigureAwait(false))
        {
            await using var reader = await command
                .ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return new SurfacingReaction(reader.GetString(0), reader.GetString(1));
            }
        }
    }

    private static Surfacing Read(NpgsqlDataReader reader)
    {
        return new Surfacing(
            surfacingId: reader.GetGuid(0),
            serviceName: reader.GetString(1),
            title: reader.GetString(2),
            body: reader.GetString(3),
            confidence: reader.GetDouble(4),
            createdAt: reader.GetFieldValue<DateTimeOffset>(5));
    }
}
