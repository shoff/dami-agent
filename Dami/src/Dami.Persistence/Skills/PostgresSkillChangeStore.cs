using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Events;
using Dami.Persistence.Events;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Dami.Persistence.Skills;

/// <summary>PostgreSQL write-ahead ledger for immutable skill changes.</summary>
public sealed class PostgresSkillChangeStore : ISkillChangeStore, ISkillChangeRecoveryStore
{
    private readonly NpgsqlDataSource dataSource;
    private readonly string eventsTable;
    private readonly string table;

    /// <summary>Creates the skill-change store.</summary>
    public PostgresSkillChangeStore(
        NpgsqlDataSource dataSource,
        IOptions<PostgresOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        this.dataSource = dataSource;
        string schema = options.Value.SchemaName;
        this.eventsTable = $"{schema}.execution_events";
        this.table = $"{schema}.skill_changes";
    }

    /// <inheritdoc />
    public async Task<SkillChangeRecord> CreateAsync(
        SkillChangeRecord record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        await using NpgsqlConnection connection = await this.dataSource
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        SkillChangeRecord accepted = await this.InsertAsync(
            connection, transaction, record, cancellationToken)
            .ConfigureAwait(false);
        await ExecutionEventCommand.AppendExactAsync(
            connection,
            transaction,
            this.eventsTable,
            SkillChangeEventFactory.Requested(accepted),
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return accepted;
    }

    /// <inheritdoc />
    public async Task<SkillChangeRecord?> FindAsync(
        Guid changeId,
        CancellationToken cancellationToken)
    {
        if (changeId == Guid.Empty)
        {
            throw new ArgumentException("A change identifier is required.", nameof(changeId));
        }

        await using NpgsqlConnection connection = await this.dataSource
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await this.FindAsync(connection, null, changeId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SkillChangeRecord>> FindPendingAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        await using NpgsqlConnection connection = await this.dataSource
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(this.PendingSql(), connection);
        command.Parameters.AddWithValue("limit", limit);
        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var records = new List<SkillChangeRecord>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            records.Add(Read(reader));
        }

        return records.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<bool> IsPendingAsync(
        Guid changeId,
        CancellationToken cancellationToken)
    {
        if (changeId == Guid.Empty)
        {
            throw new ArgumentException("A change identifier is required.", nameof(changeId));
        }

        await using NpgsqlConnection connection = await this.dataSource
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            $"""
            select exists (
                select 1 from {this.table} c
                 where c.change_id = @change
                   and not exists (
                       select 1 from {this.eventsTable} e
                        where e.type = 'SkillChanged'
                          and e.payload_reference = 'skill-change://' || c.change_id::text));
            """,
            connection);
        command.Parameters.AddWithValue("change", changeId);
        object? scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return scalar is true;
    }

    /// <inheritdoc />
    public Task RecordSucceededAsync(
        SkillChangeRecord record,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        return this.RecordOutcomeAsync(
            SkillChangeEventFactory.Succeeded(record, occurredAt), cancellationToken);
    }

    /// <inheritdoc />
    public Task RecordFailedAsync(
        SkillChangeRecord record,
        string failureCode,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        return this.RecordOutcomeAsync(
            SkillChangeEventFactory.Failed(record, failureCode, occurredAt), cancellationToken);
    }

    private async Task RecordOutcomeAsync(
        ExecutionEvent outcome,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await this.dataSource
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await ExecutionEventCommand.AppendExactAsync(
            connection, transaction, this.eventsTable, outcome, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<SkillChangeRecord> InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SkillChangeRecord record,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            insert into {this.table}
                (change_id, trace_id, span_id, parent_span_id, origin, kind, skill_id,
                 expected_version, replacement_version, replacement_document, diff, requested_at)
            values
                (@change, @trace, @span, @parent, @origin, @kind, @skill,
                 @expected, @replacement, @document, @diff, @at)
            on conflict (change_id) do nothing;
            """,
            connection,
            transaction);
        AddParameters(command, record);
        int inserted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (inserted != 0)
        {
            return record;
        }

        return await this.EnsureExactReplayAsync(
            connection, transaction, record, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SkillChangeRecord> EnsureExactReplayAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SkillChangeRecord record,
        CancellationToken cancellationToken)
    {
        SkillChangeRecord? stored = await this.FindAsync(
            connection, transaction, record.Request.ChangeId, cancellationToken)
            .ConfigureAwait(false);
        if (stored is null || !Equivalent(stored, record))
        {
            throw new InvalidOperationException(
                $"Skill change '{record.Request.ChangeId}' already exists with different data.");
        }

        return stored;
    }

    private async Task<SkillChangeRecord?> FindAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid changeId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            select change_id, trace_id, span_id, parent_span_id, origin, kind, skill_id,
                   expected_version, replacement_version, replacement_document, diff, requested_at
              from {this.table}
             where change_id = @change;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("change", changeId);
        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? Read(reader)
            : null;
    }

    private static SkillChangeRecord Read(NpgsqlDataReader reader)
    {
        SkillDocument? document = reader.IsDBNull(9)
            ? null
            : JsonSerializer.Deserialize<SkillDocument>(reader.GetString(9))
                ?? throw new InvalidDataException("Stored skill document cannot be JSON null.");
        var request = new SkillChangeRequest(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            Enum.Parse<Contracts.Events.ExecutionOrigin>(reader.GetString(4)),
            Enum.Parse<SkillChangeKind>(reader.GetString(5)),
            reader.GetGuid(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            document);
        return new SkillChangeRecord(
            request,
            reader.GetString(10),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.GetFieldValue<DateTimeOffset>(11));
    }

    private string PendingSql()
    {
        return $"""
            select c.change_id, c.trace_id, c.span_id, c.parent_span_id, c.origin,
                   c.kind, c.skill_id, c.expected_version, c.replacement_version,
                   c.replacement_document, c.diff, c.requested_at
              from {this.table} c
             where not exists (
                   select 1 from {this.eventsTable} e
                    where e.type = 'SkillChanged'
                      and e.payload_reference = 'skill-change://' || c.change_id::text)
             order by c.requested_at, c.change_id
             limit @limit;
            """;
    }

    private static void AddParameters(NpgsqlCommand command, SkillChangeRecord record)
    {
        SkillChangeRequest request = record.Request;
        command.Parameters.AddWithValue("change", request.ChangeId);
        command.Parameters.AddWithValue("trace", request.TraceId);
        command.Parameters.AddWithValue("span", request.SpanId);
        command.Parameters.AddWithValue("parent", (object?)request.ParentSpanId ?? DBNull.Value);
        command.Parameters.AddWithValue("origin", request.Origin.ToString());
        command.Parameters.AddWithValue("kind", request.Kind.ToString());
        command.Parameters.AddWithValue("skill", request.SkillId);
        command.Parameters.AddWithValue("expected", (object?)request.ExpectedVersion ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "replacement", (object?)record.ReplacementVersion ?? DBNull.Value);
        object document = request.Replacement is null
            ? DBNull.Value
            : JsonSerializer.Serialize(request.Replacement);
        command.Parameters.Add(new NpgsqlParameter("document", NpgsqlDbType.Jsonb) { Value = document });
        command.Parameters.AddWithValue("diff", record.Diff);
        command.Parameters.AddWithValue("at", record.RequestedAt);
    }

    private static bool Equivalent(SkillChangeRecord left, SkillChangeRecord right)
    {
        SkillChangeRequest first = left.Request;
        SkillChangeRequest second = right.Request;
        return first.ChangeId == second.ChangeId
            && first.TraceId == second.TraceId
            && first.SpanId == second.SpanId
            && first.ParentSpanId == second.ParentSpanId
            && first.Origin == second.Origin
            && first.Kind == second.Kind
            && first.SkillId == second.SkillId
            && string.Equals(first.ExpectedVersion, second.ExpectedVersion, StringComparison.Ordinal)
            && string.Equals(left.ReplacementVersion, right.ReplacementVersion, StringComparison.Ordinal)
            && string.Equals(left.Diff, right.Diff, StringComparison.Ordinal)
            && DocumentsEqual(first.Replacement, second.Replacement);
    }

    private static bool DocumentsEqual(SkillDocument? left, SkillDocument? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return left is not null
            && right is not null
            && left.SkillId == right.SkillId
            && string.Equals(left.Name, right.Name, StringComparison.Ordinal)
            && string.Equals(left.Description, right.Description, StringComparison.Ordinal)
            && string.Equals(left.Body, right.Body, StringComparison.Ordinal)
            && ListsEqual(left.Tags, right.Tags)
            && ListsEqual(left.RelatedCapabilities, right.RelatedCapabilities)
            && ReferencesEqual(left.References, right.References);
    }

    private static bool ListsEqual<T>(IReadOnlyList<T> left, IReadOnlyList<T> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!EqualityComparer<T>.Default.Equals(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ReferencesEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (KeyValuePair<string, string> item in left)
        {
            if (!right.TryGetValue(item.Key, out string? value)
                || !string.Equals(item.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
