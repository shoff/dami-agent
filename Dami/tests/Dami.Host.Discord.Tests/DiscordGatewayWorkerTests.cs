using Dami.Contracts.Context;
using Dami.Contracts.Gateways;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Core.Sessions;
using Dami.Core.Turns;
using Dami.Gateway.Discord;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Dami.Host.Discord.Tests;

public sealed class DiscordGatewayWorkerTests
{
    private static DiscordOptions Configured() => new()
    {
        Token = "a-token",
        OwnerUserId = "347544641295613953",
        Enabled = true,
    };

    private static async IAsyncEnumerable<InboundMessage> OneAsync(InboundMessage message)
    {
        yield return message;
        await Task.CompletedTask;
    }

    private static TurnResult Answer(PrivacyClass privacy, bool withMemory) =>
        new(
            Guid.NewGuid(),
            "the answer text",
            new AssembledContext(
                withMemory
                    ? [new RetrievedItem("observation", Guid.NewGuid(), "private", DateTimeOffset.UnixEpoch)]
                    : [],
                [],
                10),
            new ModelRoute(ModelTier.Local, privacy, "because"));

    private static IGatewayAuthority Granting()
    {
        var lease = Substitute.For<IGatewayLease>();
        var authority = Substitute.For<IGatewayAuthority>();
        authority.TryAcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(lease);
        return authority;
    }

    private static async Task RunAsync(DiscordGatewayWorker worker)
    {
        await worker.StartAsync(CancellationToken.None);
        if (worker.ExecuteTask is { } running)
        {
            await running;
        }

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Should_Not_Serve_Without_Authority()
    {
        // Two bots on one token answer every message twice and neither can see the other.
        var authority = Substitute.For<IGatewayAuthority>();
        authority.TryAcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((IGatewayLease?)null);
        var channel = Substitute.For<IEgressChannel>();

        await RunAsync(new DiscordGatewayWorker(
            authority,
            channel,
            Substitute.For<ITracedTurnRunner>(),
            Substitute.For<IProactiveRunHistory>(),
            TimeProvider.System,
            Configured(),
            NullLogger<DiscordGatewayWorker>.Instance));

        channel.DidNotReceive().ListenAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Not_Take_Authority_When_Unconfigured()
    {
        // An empty token must not cause the process to claim the gateway and lock out a
        // correctly configured one.
        var authority = Substitute.For<IGatewayAuthority>();

        await RunAsync(new DiscordGatewayWorker(
            authority,
            Substitute.For<IEgressChannel>(),
            Substitute.For<ITracedTurnRunner>(),
            Substitute.For<IProactiveRunHistory>(),
            TimeProvider.System,
            new DiscordOptions(),
            NullLogger<DiscordGatewayWorker>.Instance));

        await authority.DidNotReceive().TryAcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Send_An_Operational_Answer()
    {
        var channel = Substitute.For<IEgressChannel>();
        channel.ListenAsync(Arg.Any<CancellationToken>())
            .Returns(OneAsync(new InboundMessage("owner", "chan-1", "status?", DateTimeOffset.UnixEpoch)));
        var turns = Substitute.For<ITracedTurnRunner>();
        turns.RunTracedAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<ConversationWindow>(), Arg.Any<CancellationToken>())
            .Returns(Answer(PrivacyClass.Egressable, withMemory: false));

        await RunAsync(new DiscordGatewayWorker(
            Granting(),
            channel,
            turns,
            Substitute.For<IProactiveRunHistory>(),
            TimeProvider.System,
            Configured(),
            NullLogger<DiscordGatewayWorker>.Instance));

        await channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content => content.Text == "the answer text"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Send_Steve_His_Own_Memory_Derived_Answer()
    {
        // ADR-0025. Under ADR-0024 this refused, which meant "hi there" was answered with
        // a citation of a decision record. The recipient is the subject; it goes.
        var channel = Substitute.For<IEgressChannel>();
        channel.ListenAsync(Arg.Any<CancellationToken>())
            .Returns(OneAsync(new InboundMessage("owner", "chan-1", "where was I?", DateTimeOffset.UnixEpoch)));
        var turns = Substitute.For<ITracedTurnRunner>();
        turns.RunTracedAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<ConversationWindow>(), Arg.Any<CancellationToken>())
            .Returns(Answer(PrivacyClass.LocalOnly, withMemory: true));

        await RunAsync(new DiscordGatewayWorker(
            Granting(),
            channel,
            turns,
            Substitute.For<IProactiveRunHistory>(),
            TimeProvider.System,
            Configured(),
            NullLogger<DiscordGatewayWorker>.Instance));

        await channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content => content.Text == "the answer text"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Explain_Rather_Than_Go_Silent_When_A_Channel_Refuses()
    {
        // A future channel whose reader is not Steve still refuses. He must be told why
        // rather than watch the message disappear.
        var channel = Substitute.For<IEgressChannel>();
        channel.ListenAsync(Arg.Any<CancellationToken>())
            .Returns(OneAsync(new InboundMessage("owner", "chan-1", "where was I?", DateTimeOffset.UnixEpoch)));
        channel.SendAsync(
                Arg.Is<OutboundContent>(c => c.Provenance == ContentProvenance.ProfileDerived),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new EgressRefusedException("not addressed to the subject")));
        var turns = Substitute.For<ITracedTurnRunner>();
        turns.RunTracedAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<ConversationWindow>(), Arg.Any<CancellationToken>())
            .Returns(Answer(PrivacyClass.LocalOnly, withMemory: true));

        await RunAsync(new DiscordGatewayWorker(
            Granting(),
            channel,
            turns,
            Substitute.For<IProactiveRunHistory>(),
            TimeProvider.System,
            Configured(),
            NullLogger<DiscordGatewayWorker>.Instance));

        await channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content => content.Provenance == ContentProvenance.Operational),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Survive_A_Failing_Turn()
    {
        // The tool loop learned this the expensive way: one failure killed every turn
        // after it. A gateway that dies on a bad question is worse than useless.
        var channel = Substitute.For<IEgressChannel>();
        channel.ListenAsync(Arg.Any<CancellationToken>())
            .Returns(OneAsync(new InboundMessage("owner", "chan-1", "boom", DateTimeOffset.UnixEpoch)));
        var turns = Substitute.For<ITracedTurnRunner>();
        turns.RunTracedAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<ConversationWindow>(), Arg.Any<CancellationToken>())
            .Returns<Task<TurnResult>>(_ => throw new InvalidOperationException("model died"));

        await RunAsync(new DiscordGatewayWorker(
            Granting(),
            channel,
            turns,
            Substitute.For<IProactiveRunHistory>(),
            TimeProvider.System,
            Configured(),
            NullLogger<DiscordGatewayWorker>.Instance));
    }
}
