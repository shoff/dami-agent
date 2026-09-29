using Dami.Core.Turns;
using Xunit;

namespace Dami.Core.Tests.Turns;

public sealed class ChatObservationTests
{
    private static readonly DateTimeOffset at = new(2026, 9, 29, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Of_Should_Record_Both_Sides_As_A_Chat_Observation()
    {
        var traceId = Guid.NewGuid();

        var observation = ChatObservation.Of("what did I lift monday", "225 for five", traceId, at);

        Assert.Equal("chat", observation.Source);
        Assert.Equal(at, observation.OccurredAt);
        Assert.Equal("Steve asked: what did I lift monday — Dami answered: 225 for five", observation.Body);
        Assert.Equal(traceId.ToString(), observation.Metadata!["trace_id"]);
        Assert.False(observation.Metadata!.ContainsKey("channel"));
    }

    [Fact]
    public void Of_Should_Bound_Both_Sides()
    {
        var observation = ChatObservation.Of(new string('q', 5000), new string('a', 5000), Guid.NewGuid(), at);

        Assert.Contains(new string('q', 400) + "…", observation.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('q', 401), observation.Body, StringComparison.Ordinal);
        Assert.Contains(new string('a', 240) + "…", observation.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('a', 241), observation.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Of_Should_Name_The_Channel_When_Given()
    {
        var observation = ChatObservation.Of("hi", "hello", Guid.NewGuid(), at, "discord");

        Assert.Equal("discord", observation.Metadata!["channel"]);
    }
}
