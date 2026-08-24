using Dami.Contracts.Sessions;
using Dami.Core.Sessions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Sessions;

public sealed class ConversationWindowBuilderTests
{
    private static readonly DateTimeOffset at = new(2026, 8, 24, 7, 0, 0, TimeSpan.Zero);

    private readonly IConversationTurnStore turnStore = Substitute.For<IConversationTurnStore>();

    [Fact]
    public async Task BuildAsync_Should_Return_The_Newest_Turns_In_Conversation_Order()
    {
        var sessionId = Guid.NewGuid();
        this.turnStore.RecentCompletedTurnsAsync(sessionId, 2, Arg.Any<CancellationToken>())
            .Returns(TurnsAsync(Turn(sessionId, 2), Turn(sessionId, 3)));
        var builder = new ConversationWindowBuilder(
            this.turnStore,
            Options.Create(new SessionContextOptions
            {
                RecentTurnLimit = 2,
                MaxConversationTokens = 100,
            }));

        var window = await builder.BuildAsync(sessionId, CancellationToken.None);

        Assert.Equal(new[] { "question 2", "question 3" },
            window.Turns.Select(turn => turn.Request.Message));
        Assert.InRange(window.EstimatedTokens, 1, 100);
    }

    [Fact]
    public async Task BuildAsync_Should_Keep_The_Newest_Whole_Turns_Inside_The_Token_Budget()
    {
        var sessionId = Guid.NewGuid();
        this.turnStore.RecentCompletedTurnsAsync(sessionId, 3, Arg.Any<CancellationToken>())
            .Returns(TurnsAsync(Turn(sessionId, 1), Turn(sessionId, 2), Turn(sessionId, 3)));
        var builder = new ConversationWindowBuilder(
            this.turnStore,
            Options.Create(new SessionContextOptions
            {
                RecentTurnLimit = 3,
                MaxConversationTokens = 20,
            }));

        var window = await builder.BuildAsync(sessionId, CancellationToken.None);

        Assert.Equal("question 3", Assert.Single(window.Turns).Request.Message);
        Assert.InRange(window.EstimatedTokens, 1, 20);
    }

    [Fact]
    public async Task BuildAsync_Should_Round_Partial_Tokens_Up()
    {
        var sessionId = Guid.NewGuid();
        var request = new ConversationTurnRequest(sessionId, Guid.NewGuid(), "q", at);
        var turn = new ConversationTurn(
            1, request, Guid.NewGuid(), ConversationTurnState.Completed, string.Empty, at);
        this.turnStore.RecentCompletedTurnsAsync(sessionId, 1, Arg.Any<CancellationToken>())
            .Returns(TurnsAsync(turn));
        var builder = new ConversationWindowBuilder(
            this.turnStore,
            Options.Create(new SessionContextOptions
            {
                RecentTurnLimit = 1,
                MaxConversationTokens = 13,
            }));

        var window = await builder.BuildAsync(sessionId, CancellationToken.None);

        Assert.Equal(13, window.EstimatedTokens);
    }

    [Fact]
    public async Task BuildAsync_Should_Not_Observe_Options_Mutated_After_Construction()
    {
        var sessionId = Guid.NewGuid();
        this.turnStore.RecentCompletedTurnsAsync(sessionId, 2, Arg.Any<CancellationToken>())
            .Returns(TurnsAsync(Turn(sessionId, 1), Turn(sessionId, 2)));
        var options = new SessionContextOptions
        {
            RecentTurnLimit = 2,
            MaxConversationTokens = 100,
        };
        var builder = new ConversationWindowBuilder(this.turnStore, Options.Create(options));
        options.RecentTurnLimit = 1;

        var window = await builder.BuildAsync(sessionId, CancellationToken.None);

        Assert.Equal(2, window.Turns.Count);
    }

    private static ConversationTurn Turn(Guid sessionId, int index)
    {
        var request = new ConversationTurnRequest(
            sessionId, Guid.NewGuid(), $"question {index}", at.AddMinutes(index));
        return new ConversationTurn(
            index, request, Guid.NewGuid(), ConversationTurnState.Completed,
            $"answer {index}", at.AddMinutes(index + 1));
    }

    private static async IAsyncEnumerable<ConversationTurn> TurnsAsync(
        params ConversationTurn[] turns)
    {
        foreach (var turn in turns)
        {
            yield return turn;
        }

        await Task.CompletedTask;
    }
}
