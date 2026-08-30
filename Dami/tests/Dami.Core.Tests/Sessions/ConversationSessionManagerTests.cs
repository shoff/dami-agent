using Dami.Contracts.Sessions;
using Dami.Core.Sessions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Sessions;

public sealed class ConversationSessionManagerTests
{
    private static readonly DateTimeOffset at = new(2026, 8, 24, 10, 0, 0, TimeSpan.Zero);

    private readonly IConversationSessionStore sessionStore =
        Substitute.For<IConversationSessionStore>();
    private readonly ISessionCancellationRegistry cancellationRegistry =
        Substitute.For<ISessionCancellationRegistry>();

    [Fact]
    public async Task StartAsync_Should_Create_An_Active_Session_With_The_Stable_Client_Id()
    {
        var sessionId = Guid.NewGuid();
        this.sessionStore.FindAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns((ConversationSession?)null);

        var session = await this.CreateManager().StartAsync(sessionId, CancellationToken.None);

        Assert.Equal(sessionId, session.SessionId);
        Assert.Equal(ConversationSessionState.Active, session.State);
        Assert.Equal(at, session.CreatedAt);
        await this.sessionStore.Received(1).CreateAsync(session, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_Should_Converge_When_The_Same_Id_Is_Created_Concurrently()
    {
        var sessionId = Guid.NewGuid();
        var winner = new ConversationSession(
            sessionId, ConversationSessionState.Active, at.AddSeconds(-1), at.AddSeconds(-1));
        this.sessionStore.FindAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns((ConversationSession?)null, winner);
        this.sessionStore.CreateAsync(Arg.Any<ConversationSession>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("conflicting insert"));

        var session = await this.CreateManager().StartAsync(sessionId, CancellationToken.None);

        Assert.Equal(winner, session);
    }

    [Fact]
    public async Task ResumeAsync_Should_Transition_An_Interrupted_Session_To_Active()
    {
        var sessionId = Guid.NewGuid();
        var interrupted = new ConversationSession(
            sessionId, ConversationSessionState.Interrupted, at.AddMinutes(-1), at.AddSeconds(-1));
        var active = new ConversationSession(
            sessionId, ConversationSessionState.Active, interrupted.CreatedAt, at);
        this.sessionStore.FindAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns(interrupted, active);
        this.sessionStore.TryTransitionAsync(
                sessionId, ConversationSessionState.Interrupted, ConversationSessionState.Active,
                at, Arg.Any<CancellationToken>())
            .Returns(true);

        var session = await this.CreateManager().ResumeAsync(sessionId, CancellationToken.None);

        Assert.Equal(active, session);
    }

    [Fact]
    public async Task InterruptAsync_Should_Transition_An_Active_Session_To_Interrupted()
    {
        var sessionId = Guid.NewGuid();
        var active = new ConversationSession(
            sessionId, ConversationSessionState.Active, at.AddMinutes(-1), at.AddSeconds(-1));
        var interrupted = new ConversationSession(
            sessionId, ConversationSessionState.Interrupted, active.CreatedAt, at);
        this.sessionStore.FindAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns(active, interrupted);
        this.sessionStore.TryTransitionAsync(
                sessionId, ConversationSessionState.Active, ConversationSessionState.Interrupted,
                at, Arg.Any<CancellationToken>())
            .Returns(true);

        var session = await this.CreateManager().InterruptAsync(sessionId, CancellationToken.None);

        Assert.Equal(interrupted, session);
    }

    [Fact]
    public async Task InterruptAsync_Should_Cancel_The_Active_Execution_Generation()
    {
        var sessionId = Guid.NewGuid();
        var interrupted = new ConversationSession(
            sessionId, ConversationSessionState.Interrupted, at.AddMinutes(-1), at);
        this.sessionStore.FindAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns(interrupted);

        await this.CreateManager().InterruptAsync(sessionId, CancellationToken.None);

        await this.cancellationRegistry.Received(1).InterruptAsync(sessionId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeAsync_Should_Start_A_Fresh_Execution_Generation()
    {
        var sessionId = Guid.NewGuid();
        var active = new ConversationSession(
            sessionId, ConversationSessionState.Active, at.AddMinutes(-1), at);
        this.sessionStore.FindAsync(sessionId, Arg.Any<CancellationToken>()).Returns(active);

        await this.CreateManager().ResumeAsync(sessionId, CancellationToken.None);

        this.cancellationRegistry.Received(1).Resume(sessionId);
    }

    private ConversationSessionManager CreateManager()
    {
        return new ConversationSessionManager(
            this.sessionStore, this.cancellationRegistry, new FakeTimeProvider(at));
    }
}
