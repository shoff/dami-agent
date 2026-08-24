using Dami.Contracts.Sessions;
using Dami.Persistence.Sessions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Sessions;

[Collection(DatabaseCollection.NAME)]
public sealed class PostgresSessionStoreTests
{
    private static readonly DateTimeOffset createdAt =
        new(2026, 8, 24, 6, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public PostgresSessionStoreTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public void ConversationSession_Should_Reject_An_Unknown_State()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConversationSession(
            Guid.NewGuid(), (ConversationSessionState)99, createdAt, createdAt));
    }

    [Fact]
    public void ConversationTurn_Should_Reject_An_Unknown_State()
    {
        var request = new ConversationTurnRequest(
            Guid.NewGuid(), Guid.NewGuid(), "hello", createdAt);

        Assert.Throws<ArgumentOutOfRangeException>(() => new ConversationTurn(
            1, request, Guid.NewGuid(), (ConversationTurnState)99,
            completedAt: createdAt.AddMinutes(1)));
    }

    [Fact]
    public void ConversationTurn_Should_Reject_Completion_Before_The_Request()
    {
        var request = new ConversationTurnRequest(
            Guid.NewGuid(), Guid.NewGuid(), "hello", createdAt);

        Assert.Throws<ArgumentOutOfRangeException>(() => new ConversationTurn(
            1, request, Guid.NewGuid(), ConversationTurnState.Completed, "answer",
            createdAt.AddMinutes(-1)));
    }

    [Fact]
    public void ConversationTurnReservation_Should_Reject_A_Null_Turn()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ConversationTurnReservation(null!, false));
    }

    [Fact]
    public async Task CreateAsync_Should_Round_Trip_An_Active_Session()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var session = new ConversationSession(
            Guid.NewGuid(), ConversationSessionState.Active, createdAt, createdAt);

        await store.CreateAsync(session, CancellationToken.None);
        var found = await store.FindAsync(session.SessionId, CancellationToken.None);

        Assert.Equal(session, found);
    }

    [Fact]
    public async Task TryTransitionAsync_Should_Interrupt_Only_An_Active_Session()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var session = new ConversationSession(
            Guid.NewGuid(), ConversationSessionState.Active, createdAt, createdAt);
        await store.CreateAsync(session, CancellationToken.None);

        var changed = await store.TryTransitionAsync(
            session.SessionId, ConversationSessionState.Active,
            ConversationSessionState.Interrupted, createdAt.AddMinutes(1), CancellationToken.None);
        var stale = await store.TryTransitionAsync(
            session.SessionId, ConversationSessionState.Active,
            ConversationSessionState.Interrupted, createdAt.AddMinutes(2), CancellationToken.None);

        Assert.True(changed);
        Assert.False(stale);
        Assert.Equal(ConversationSessionState.Interrupted,
            (await store.FindAsync(session.SessionId, CancellationToken.None))!.State);
    }

    [Fact]
    public async Task TryTransitionAsync_Should_Interrupt_All_Running_Turns_Atomically()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var session = await this.CreateSessionAsync(store);
        var completed = new ConversationTurnRequest(
            session.SessionId, Guid.NewGuid(), "done", createdAt.AddMinutes(1));
        var running = new ConversationTurnRequest(
            session.SessionId, Guid.NewGuid(), "running", createdAt.AddMinutes(2));
        await store.ReserveTurnAsync(completed, CancellationToken.None);
        await store.CompleteTurnAsync(
            session.SessionId, completed.RequestId, "answer",
            createdAt.AddMinutes(3), CancellationToken.None);
        await store.ReserveTurnAsync(running, CancellationToken.None);

        await store.TryTransitionAsync(
            session.SessionId, ConversationSessionState.Active,
            ConversationSessionState.Interrupted, createdAt.AddMinutes(4), CancellationToken.None);

        var completedTurn = await store.FindTurnAsync(
            session.SessionId, completed.RequestId, CancellationToken.None);
        var interruptedTurn = await store.FindTurnAsync(
            session.SessionId, running.RequestId, CancellationToken.None);
        Assert.Equal((ConversationTurnState.Completed, ConversationTurnState.Interrupted),
            (completedTurn!.State, interruptedTurn!.State));
    }

    [Fact]
    public async Task ReserveTurnAsync_Should_Create_A_Running_Turn_With_A_Stable_Trace()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var session = new ConversationSession(
            Guid.NewGuid(), ConversationSessionState.Active, createdAt, createdAt);
        await store.CreateAsync(session, CancellationToken.None);
        var request = new ConversationTurnRequest(
            session.SessionId, Guid.NewGuid(), "hello", createdAt.AddMinutes(1));

        var reservation = await store.ReserveTurnAsync(request, CancellationToken.None);

        Assert.True(reservation.IsNew);
        Assert.Equal(request, reservation.Turn.Request);
        Assert.Equal(ConversationTurnState.Running, reservation.Turn.State);
        Assert.NotEqual(Guid.Empty, reservation.Turn.TraceId);
        Assert.True(reservation.Turn.Sequence > 0);
    }

    [Fact]
    public async Task CompleteTurnAsync_Should_Complete_Only_A_Running_Turn()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var session = new ConversationSession(
            Guid.NewGuid(), ConversationSessionState.Active, createdAt, createdAt);
        await store.CreateAsync(session, CancellationToken.None);
        var request = new ConversationTurnRequest(
            session.SessionId, Guid.NewGuid(), "hello", createdAt.AddMinutes(1));
        await store.ReserveTurnAsync(request, CancellationToken.None);

        var changed = await store.CompleteTurnAsync(
            request.SessionId, request.RequestId, "hello back",
            createdAt.AddMinutes(2), CancellationToken.None);
        var stale = await store.CompleteTurnAsync(
            request.SessionId, request.RequestId, "duplicate",
            createdAt.AddMinutes(3), CancellationToken.None);
        var turn = await store.FindTurnAsync(
            request.SessionId, request.RequestId, CancellationToken.None);

        Assert.Equal((true, false), (changed, stale));
        Assert.Equal((ConversationTurnState.Completed, "hello back"), (turn!.State, turn.Response));
    }

    [Fact]
    public async Task InterruptTurnAsync_Should_Prevent_A_Late_Completion()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var session = new ConversationSession(
            Guid.NewGuid(), ConversationSessionState.Active, createdAt, createdAt);
        await store.CreateAsync(session, CancellationToken.None);
        var request = new ConversationTurnRequest(
            session.SessionId, Guid.NewGuid(), "wait", createdAt.AddMinutes(1));
        await store.ReserveTurnAsync(request, CancellationToken.None);

        var interrupted = await store.InterruptTurnAsync(
            request.SessionId, request.RequestId, createdAt.AddMinutes(2), CancellationToken.None);
        var completed = await store.CompleteTurnAsync(
            request.SessionId, request.RequestId, "too late",
            createdAt.AddMinutes(3), CancellationToken.None);
        var turn = await store.FindTurnAsync(
            request.SessionId, request.RequestId, CancellationToken.None);

        Assert.Equal((true, false, ConversationTurnState.Interrupted),
            (interrupted, completed, turn!.State));
    }

    [Fact]
    public async Task RecentCompletedTurnsAsync_Should_Return_A_Bounded_Conversational_Window()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var session = new ConversationSession(
            Guid.NewGuid(), ConversationSessionState.Active, createdAt, createdAt);
        await store.CreateAsync(session, CancellationToken.None);
        for (var index = 0; index < 4; index++)
        {
            await this.CompleteTurnAsync(store, session.SessionId, $"turn {index}", index);
        }

        await store.ReserveTurnAsync(
            new ConversationTurnRequest(
                session.SessionId, Guid.NewGuid(), "still running", createdAt.AddHours(1)),
            CancellationToken.None);
        var recent = new List<ConversationTurn>();
        await foreach (var turn in store.RecentCompletedTurnsAsync(
            session.SessionId, 2, CancellationToken.None))
        {
            recent.Add(turn);
        }

        Assert.Equal(new[] { "turn 2", "turn 3" }, recent.Select(turn => turn.Request.Message));
    }

    [Fact]
    public async Task ListRecentAsync_Should_Bound_And_Order_Sessions_By_Activity()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var sessions = Enumerable.Range(0, 3)
            .Select(index => new ConversationSession(
                Guid.NewGuid(), ConversationSessionState.Active,
                createdAt, createdAt.AddMinutes(index)))
            .ToArray();
        foreach (var session in sessions)
        {
            await store.CreateAsync(session, CancellationToken.None);
        }

        var recent = new List<ConversationSession>();
        await foreach (var session in store.ListRecentAsync(2, CancellationToken.None))
        {
            recent.Add(session);
        }

        Assert.Equal(new[] { sessions[2].SessionId, sessions[1].SessionId },
            recent.Select(session => session.SessionId));
    }

    [Fact]
    public async Task FailTurnAsync_Should_Terminate_A_Running_Turn_Without_A_Response()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var session = new ConversationSession(
            Guid.NewGuid(), ConversationSessionState.Active, createdAt, createdAt);
        await store.CreateAsync(session, CancellationToken.None);
        var request = new ConversationTurnRequest(
            session.SessionId, Guid.NewGuid(), "fail", createdAt.AddMinutes(1));
        await store.ReserveTurnAsync(request, CancellationToken.None);

        var failed = await store.FailTurnAsync(
            request.SessionId, request.RequestId, createdAt.AddMinutes(2), CancellationToken.None);
        var turn = await store.FindTurnAsync(
            request.SessionId, request.RequestId, CancellationToken.None);

        Assert.True(failed);
        Assert.Equal((ConversationTurnState.Failed, null), (turn!.State, turn.Response));
    }

    [Fact]
    public async Task ReserveTurnAsync_Should_Return_The_Same_Trace_For_An_Exact_Retry()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var session = await this.CreateSessionAsync(store);
        var request = new ConversationTurnRequest(
            session.SessionId, Guid.NewGuid(), "retry me", createdAt.AddMinutes(1));

        var first = await store.ReserveTurnAsync(request, CancellationToken.None);
        var retry = await store.ReserveTurnAsync(request, CancellationToken.None);

        Assert.True(first.IsNew);
        Assert.False(retry.IsNew);
        Assert.Equal((first.Turn.TraceId, first.Turn.Sequence),
            (retry.Turn.TraceId, retry.Turn.Sequence));
    }

    [Fact]
    public async Task ReserveTurnAsync_Should_Reject_A_Conflicting_Request_Id_Replay()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var session = await this.CreateSessionAsync(store);
        var request = new ConversationTurnRequest(
            session.SessionId, Guid.NewGuid(), "original", createdAt.AddMinutes(1));
        await store.ReserveTurnAsync(request, CancellationToken.None);
        var conflicting = new ConversationTurnRequest(
            session.SessionId, request.RequestId, "different", createdAt.AddMinutes(2));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.ReserveTurnAsync(conflicting, CancellationToken.None));
    }

    [Fact]
    public async Task ReserveTurnAsync_Should_Converge_Concurrent_Exact_Retries()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var session = await this.CreateSessionAsync(store);
        var request = new ConversationTurnRequest(
            session.SessionId, Guid.NewGuid(), "race", createdAt.AddMinutes(1));

        var reservations = await Task.WhenAll(
            store.ReserveTurnAsync(request, CancellationToken.None),
            store.ReserveTurnAsync(request, CancellationToken.None));

        Assert.Single(reservations, reservation => reservation.IsNew);
        Assert.Single(reservations.Select(reservation => reservation.Turn.TraceId).Distinct());
    }

    [Fact]
    public async Task ReserveTurnAsync_Should_Reject_A_New_Turn_In_An_Interrupted_Session()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var session = await this.CreateSessionAsync(store);
        await store.TryTransitionAsync(
            session.SessionId, ConversationSessionState.Active,
            ConversationSessionState.Interrupted, createdAt.AddMinutes(1), CancellationToken.None);
        var request = new ConversationTurnRequest(
            session.SessionId, Guid.NewGuid(), "blocked", createdAt.AddMinutes(2));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.ReserveTurnAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task Terminal_Transitions_Should_Have_Exactly_One_Winner()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var session = await this.CreateSessionAsync(store);
        var request = new ConversationTurnRequest(
            session.SessionId, Guid.NewGuid(), "race", createdAt.AddMinutes(1));
        await store.ReserveTurnAsync(request, CancellationToken.None);

        var outcomes = await Task.WhenAll(
            store.CompleteTurnAsync(
                session.SessionId, request.RequestId, "done", createdAt.AddMinutes(2), CancellationToken.None),
            store.InterruptTurnAsync(
                session.SessionId, request.RequestId, createdAt.AddMinutes(2), CancellationToken.None));

        Assert.Single(outcomes, outcome => outcome);
    }

    [Fact]
    public async Task Database_Should_Keep_Immutable_Session_And_Turn_Columns_Outside_App_Update()
    {
        await this.fixture.ResetAsync();
        await using var command = this.fixture.DataSource.CreateCommand(
            $"""
            select has_column_privilege('dami_app', '{DatabaseFixture.SCHEMA}.conversation_sessions', 'state', 'update')
               and has_column_privilege('dami_app', '{DatabaseFixture.SCHEMA}.conversation_sessions', 'updated_at', 'update')
               and not has_column_privilege('dami_app', '{DatabaseFixture.SCHEMA}.conversation_sessions', 'session_id', 'update')
               and not has_column_privilege('dami_app', '{DatabaseFixture.SCHEMA}.conversation_sessions', 'created_at', 'update')
               and has_column_privilege('dami_app', '{DatabaseFixture.SCHEMA}.conversation_turns', 'state', 'update')
               and has_column_privilege('dami_app', '{DatabaseFixture.SCHEMA}.conversation_turns', 'completed_at', 'update')
               and not has_column_privilege('dami_app', '{DatabaseFixture.SCHEMA}.conversation_turns', 'user_message', 'update')
               and not has_table_privilege('dami_app', '{DatabaseFixture.SCHEMA}.conversation_turns', 'delete')
               and has_sequence_privilege('dami_app', '{DatabaseFixture.SCHEMA}.conversation_turns_sequence_seq', 'usage');
            """);

        Assert.Equal(true, await command.ExecuteScalarAsync(CancellationToken.None));
    }

    private async Task CompleteTurnAsync(
        PostgresSessionStore store,
        Guid sessionId,
        string message,
        int minuteOffset)
    {
        var request = new ConversationTurnRequest(
            sessionId, Guid.NewGuid(), message, createdAt.AddMinutes(minuteOffset + 1));
        await store.ReserveTurnAsync(request, CancellationToken.None);
        await store.CompleteTurnAsync(
            sessionId, request.RequestId, $"response {minuteOffset}",
            createdAt.AddMinutes(minuteOffset + 2), CancellationToken.None);
    }

    private async Task<ConversationSession> CreateSessionAsync(PostgresSessionStore store)
    {
        var session = new ConversationSession(
            Guid.NewGuid(), ConversationSessionState.Active, createdAt, createdAt);
        await store.CreateAsync(session, CancellationToken.None);
        return session;
    }

    private PostgresSessionStore CreateStore()
    {
        return new PostgresSessionStore(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));
    }
}
