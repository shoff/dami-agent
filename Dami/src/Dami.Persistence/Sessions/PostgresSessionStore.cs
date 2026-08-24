using System.Runtime.CompilerServices;
using Dami.Contracts.Sessions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Sessions;

/// <summary>Conversation-session lifecycle storage over PostgreSQL.</summary>
public sealed class PostgresSessionStore : IConversationSessionStore, IConversationTurnStore
{
    private readonly NpgsqlDataSource dataSource;
    private readonly string table;
    private readonly string turnsTable;

    /// <summary>Creates the store.</summary>
    public PostgresSessionStore(
        NpgsqlDataSource dataSource,
        IOptions<PostgresOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        this.dataSource = dataSource;
        this.table = $"{options.Value.SchemaName}.conversation_sessions";
        this.turnsTable = $"{options.Value.SchemaName}.conversation_turns";
    }

    /// <inheritdoc />
    public async Task CreateAsync(
        ConversationSession session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        await using var command = this.dataSource.CreateCommand(
            $"""
            insert into {this.table} (session_id, state, created_at, updated_at)
            values (@id, @state, @created, @updated)
            on conflict (session_id) do nothing;
            """);
        AddParameters(command, session);
        var inserted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (inserted == 0 && !await this.IsExactAsync(session, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"Session '{session.SessionId}' conflicts with its durable stored value.");
        }
    }

    /// <inheritdoc />
    public async Task<ConversationSession?> FindAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"""
            select session_id, state, created_at, updated_at
              from {this.table}
             where session_id = @id;
            """);
        command.Parameters.AddWithValue("id", sessionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? await ReadSessionAsync(reader, cancellationToken).ConfigureAwait(false)
            : null;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ConversationSession> ListRecentAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var command = this.dataSource.CreateCommand(
            $"""
            select session_id, state, created_at, updated_at
              from {this.table}
             order by updated_at desc, session_id
             limit @limit;
            """);
        command.Parameters.AddWithValue("limit", limit);
        return StreamSessionsAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TryTransitionAsync(
        Guid sessionId,
        ConversationSessionState expected,
        ConversationSessionState next,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        if (expected == next)
        {
            throw new ArgumentException("A session transition must change state.", nameof(next));
        }

        await using var connection = await this.dataSource
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var changed = await this.UpdateSessionStateAsync(
            connection, transaction, sessionId, expected, next, updatedAt, cancellationToken)
            .ConfigureAwait(false);
        if (changed && next == ConversationSessionState.Interrupted)
        {
            await this.InterruptRunningTurnsAsync(
                connection, transaction, sessionId, updatedAt, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return changed;
    }

    /// <inheritdoc />
    public async Task<ConversationTurnReservation> ReserveTurnAsync(
        ConversationTurnRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var connection = await this.dataSource
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var existing = await this.FindReservationAsync(
            connection, transaction, request, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return existing;
        }

        await this.LockActiveSessionAsync(
            connection, transaction, request.SessionId, cancellationToken).ConfigureAwait(false);
        var inserted = await this.InsertTurnAsync(
            connection, transaction, request, Guid.NewGuid(), cancellationToken).ConfigureAwait(false);
        var turn = inserted ?? await this.FindTurnAsync(
            connection, transaction, request.SessionId, request.RequestId, cancellationToken)
            .ConfigureAwait(false);
        EnsureExactRequest(turn, request);
        if (inserted is not null)
        {
            await this.TouchSessionAsync(
                connection, transaction, request.SessionId, request.RequestedAt, cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new ConversationTurnReservation(turn!, inserted is not null);
    }

    private async Task<ConversationTurnReservation?> FindReservationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ConversationTurnRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await this.FindTurnAsync(
            connection, transaction, request.SessionId, request.RequestId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return null;
        }

        EnsureExactRequest(existing, request);
        return new ConversationTurnReservation(existing, false);
    }

    private async Task<bool> UpdateSessionStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid sessionId,
        ConversationSessionState expected,
        ConversationSessionState next,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            update {this.table}
               set state = @next, updated_at = @updated
             where session_id = @id and state = @expected and updated_at <= @updated;
            """, connection, transaction);
        command.Parameters.AddWithValue("id", sessionId);
        command.Parameters.AddWithValue("expected", expected.ToString());
        command.Parameters.AddWithValue("next", next.ToString());
        command.Parameters.AddWithValue("updated", updatedAt);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    private async Task InterruptRunningTurnsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid sessionId,
        DateTimeOffset interruptedAt,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            update {this.turnsTable}
               set state = 'Interrupted', completed_at = @at
             where session_id = @session and state = 'Running';
            """, connection, transaction);
        command.Parameters.AddWithValue("session", sessionId);
        command.Parameters.AddWithValue("at", interruptedAt);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task LockActiveSessionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"select session_id from {this.table} where session_id = @id and state = 'Active' for update;",
            connection,
            transaction);
        command.Parameters.AddWithValue("id", sessionId);
        if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not Guid)
        {
            throw new InvalidOperationException(
                $"Session '{sessionId}' is unknown or does not accept new turns.");
        }
    }

    /// <inheritdoc />
    public async Task<bool> CompleteTurnAsync(
        Guid sessionId,
        Guid requestId,
        string response,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        return await this.TransitionTurnAsync(
            sessionId, requestId, ConversationTurnState.Completed,
            response, completedAt, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<bool> InterruptTurnAsync(
        Guid sessionId,
        Guid requestId,
        DateTimeOffset interruptedAt,
        CancellationToken cancellationToken)
    {
        return this.TransitionTurnAsync(
            sessionId, requestId, ConversationTurnState.Interrupted,
            null, interruptedAt, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> FailTurnAsync(
        Guid sessionId,
        Guid requestId,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken)
    {
        return this.TransitionTurnAsync(
            sessionId, requestId, ConversationTurnState.Failed,
            null, failedAt, cancellationToken);
    }

    private async Task<bool> TransitionTurnAsync(
        Guid sessionId,
        Guid requestId,
        ConversationTurnState state,
        string? response,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await this.dataSource
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var turn = await this.UpdateTerminalAsync(
            connection, transaction, sessionId, requestId, state, response, completedAt, cancellationToken)
            .ConfigureAwait(false);
        if (turn is null)
        {
            return false;
        }

        await this.TouchSessionAsync(
            connection, transaction, sessionId, completedAt, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task<ConversationTurn?> FindTurnAsync(
        Guid sessionId,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        await using var connection = await this.dataSource
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await this.FindTurnAsync(
            connection, null, sessionId, requestId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ConversationTurn> RecentCompletedTurnsAsync(
        Guid sessionId,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var command = this.dataSource.CreateCommand(
            $"""
            select sequence, session_id, request_id, trace_id, user_message,
                   assistant_message, state, requested_at, completed_at
              from (
                select sequence, session_id, request_id, trace_id, user_message,
                       assistant_message, state, requested_at, completed_at
                  from {this.turnsTable}
                 where session_id = @session and state = 'Completed'
                 order by sequence desc limit @limit
              ) recent
             order by sequence;
            """);
        command.Parameters.AddWithValue("session", sessionId);
        command.Parameters.AddWithValue("limit", limit);
        return StreamTurnsAsync(command, cancellationToken);
    }

    private async Task<ConversationTurn?> UpdateTerminalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid sessionId,
        Guid requestId,
        ConversationTurnState state,
        string? response,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            update {this.turnsTable}
               set assistant_message = @response, state = @state, completed_at = @completed
             where session_id = @session and request_id = @request and state = 'Running'
             returning sequence, session_id, request_id, trace_id, user_message,
                       assistant_message, state, requested_at, completed_at;
            """, connection, transaction);
        command.Parameters.AddWithValue("session", sessionId);
        command.Parameters.AddWithValue("request", requestId);
        command.Parameters.AddWithValue("state", state.ToString());
        command.Parameters.AddWithValue("response", (object?)response ?? DBNull.Value);
        command.Parameters.AddWithValue("completed", completedAt);
        return await ReadTurnAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ConversationTurn?> InsertTurnAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ConversationTurnRequest request,
        Guid traceId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            insert into {this.turnsTable}
                (session_id, request_id, trace_id, user_message, state, requested_at)
            values (@session, @request, @trace, @message, 'Running', @requested)
            on conflict (session_id, request_id) do nothing
            returning sequence, session_id, request_id, trace_id, user_message,
                      assistant_message, state, requested_at, completed_at;
            """, connection, transaction);
        AddTurnParameters(command, request, traceId);
        return await ReadTurnAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ConversationTurn?> FindTurnAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid sessionId,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            select sequence, session_id, request_id, trace_id, user_message,
                   assistant_message, state, requested_at, completed_at
              from {this.turnsTable}
             where session_id = @session and request_id = @request;
            """, connection, transaction);
        command.Parameters.AddWithValue("session", sessionId);
        command.Parameters.AddWithValue("request", requestId);
        return await ReadTurnAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private async Task TouchSessionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid sessionId,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"update {this.table} set updated_at = greatest(updated_at, @at) where session_id = @id;",
            connection,
            transaction);
        command.Parameters.AddWithValue("id", sessionId);
        command.Parameters.AddWithValue("at", requestedAt);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureExactRequest(
        ConversationTurn? turn,
        ConversationTurnRequest request)
    {
        if (turn is null)
        {
            throw new InvalidOperationException(
                $"Session '{request.SessionId}' is unknown or does not accept new turns.");
        }

        if (!string.Equals(turn.Request.Message, request.Message, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Request '{request.RequestId}' conflicts with its durable stored message.");
        }
    }

    private static void AddTurnParameters(
        NpgsqlCommand command,
        ConversationTurnRequest request,
        Guid traceId)
    {
        command.Parameters.AddWithValue("session", request.SessionId);
        command.Parameters.AddWithValue("request", request.RequestId);
        command.Parameters.AddWithValue("trace", traceId);
        command.Parameters.AddWithValue("message", request.Message);
        command.Parameters.AddWithValue("requested", request.RequestedAt);
    }

    private static async Task<ConversationTurn?> ReadTurnAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await ReadTurnAsync(reader, cancellationToken).ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<ConversationTurn> StreamTurnsAsync(
        NpgsqlCommand command,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using (command.ConfigureAwait(false))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return await ReadTurnAsync(reader, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<ConversationTurn> ReadTurnAsync(
        NpgsqlDataReader reader,
        CancellationToken cancellationToken)
    {
        var requestedAt = await reader.GetFieldValueAsync<DateTimeOffset>(7, cancellationToken)
            .ConfigureAwait(false);
        var responseIsNull = await reader.IsDBNullAsync(5, cancellationToken).ConfigureAwait(false);
        var completedIsNull = await reader.IsDBNullAsync(8, cancellationToken).ConfigureAwait(false);
        DateTimeOffset? completedAt = completedIsNull
            ? null
            : await reader.GetFieldValueAsync<DateTimeOffset>(8, cancellationToken).ConfigureAwait(false);
        var request = new ConversationTurnRequest(
            reader.GetGuid(1), reader.GetGuid(2), reader.GetString(4), requestedAt);
        return new ConversationTurn(
            reader.GetInt64(0), request, reader.GetGuid(3),
            Enum.Parse<ConversationTurnState>(reader.GetString(6)),
            responseIsNull ? null : reader.GetString(5), completedAt);
    }

    private async Task<bool> IsExactAsync(
        ConversationSession session,
        CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"""
            select exists (
                select 1 from {this.table}
                 where session_id = @id and state = @state
                   and created_at = @created and updated_at = @updated);
            """);
        AddParameters(command, session);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
    }

    private static void AddParameters(NpgsqlCommand command, ConversationSession session)
    {
        command.Parameters.AddWithValue("id", session.SessionId);
        command.Parameters.AddWithValue("state", session.State.ToString());
        command.Parameters.AddWithValue("created", session.CreatedAt);
        command.Parameters.AddWithValue("updated", session.UpdatedAt);
    }

    private static async IAsyncEnumerable<ConversationSession> StreamSessionsAsync(
        NpgsqlCommand command,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using (command.ConfigureAwait(false))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return await ReadSessionAsync(reader, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<ConversationSession> ReadSessionAsync(
        NpgsqlDataReader reader,
        CancellationToken cancellationToken)
    {
        var createdAt = await reader.GetFieldValueAsync<DateTimeOffset>(2, cancellationToken)
            .ConfigureAwait(false);
        var updatedAt = await reader.GetFieldValueAsync<DateTimeOffset>(3, cancellationToken)
            .ConfigureAwait(false);
        return new ConversationSession(
            reader.GetGuid(0),
            Enum.Parse<ConversationSessionState>(reader.GetString(1)),
            createdAt,
            updatedAt);
    }
}
