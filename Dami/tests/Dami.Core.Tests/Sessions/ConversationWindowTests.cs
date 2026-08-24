using Dami.Contracts.Sessions;
using Dami.Core.Sessions;
using Xunit;

namespace Dami.Core.Tests.Sessions;

public sealed class ConversationWindowTests
{
    private static readonly DateTimeOffset at = new(2026, 8, 24, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_Should_Snapshot_The_Provided_Turns()
    {
        var turns = new List<ConversationTurn> { CompletedTurn() };

        var window = new ConversationWindow(turns, 20);
        turns.Clear();

        Assert.Single(window.Turns);
    }

    [Fact]
    public void Constructor_Should_Reject_A_Noncompleted_Turn()
    {
        var request = new ConversationTurnRequest(Guid.NewGuid(), Guid.NewGuid(), "question", at);
        var running = new ConversationTurn(
            1, request, Guid.NewGuid(), ConversationTurnState.Running);

        Assert.Throws<ArgumentException>(() => new ConversationWindow([running], 20));
    }

    [Fact]
    public void Turns_Should_Not_Expose_A_Mutable_Array()
    {
        var window = new ConversationWindow([CompletedTurn()], 20);
        var exposed = Assert.IsAssignableFrom<IList<ConversationTurn>>(window.Turns);

        Assert.Throws<NotSupportedException>(() => exposed[0] = CompletedTurn());
    }

    private static ConversationTurn CompletedTurn()
    {
        var request = new ConversationTurnRequest(Guid.NewGuid(), Guid.NewGuid(), "question", at);
        return new ConversationTurn(
            1, request, Guid.NewGuid(), ConversationTurnState.Completed, "answer", at);
    }
}
