using Dami.Contracts.Domains;
using Dami.Contracts.Privacy;
using Dami.Gateway.Discord;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Host.Discord.Tests;

/// <summary>ADR-0037 slice 3: a warfarin interactor in forwarded mail is noticed like one Steve said.</summary>
public sealed class DiscordMailInteractionWatchTests : IDisposable
{
    private static readonly DateTimeOffset now = new(2026, 10, 3, 15, 0, 0, TimeSpan.Zero);

    private readonly IDomainFactStore facts = Substitute.For<IDomainFactStore>();
    private readonly IEgressChannel channel = Substitute.For<IEgressChannel>();
    private readonly string state = Path.Combine(Path.GetTempPath(), "dami-mailwatch-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => File.Delete(this.state);

    private static DomainFact Filed(string description, DateTimeOffset recorded) =>
        new(Guid.NewGuid(), "mail", DateOnly.FromDateTime(recorded.UtcDateTime), "other", description, "forwarded mail", recorded);

    private static async IAsyncEnumerable<DomainFact> ManyAsync(params DomainFact[] items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }

    private DiscordMailInteractionWatch Subject(DateTimeOffset at) => new(
        this.facts,
        new DiscordInteractionWatch(this.channel, new FakeTimeProvider(at), NullLogger<DiscordInteractionWatch>.Instance),
        new DiscordOptions { Token = "t", OwnerUserId = "1", Enabled = true, CheckInConversationId = "dm-7" },
        new FakeTimeProvider(at), this.state, NullLogger<DiscordMailInteractionWatch>.Instance);

    [Fact]
    public async Task A_Newly_Filed_Mail_Naming_An_Interactor_Should_Be_Noticed_Once()
    {
        this.facts.TimelineAsync("mail", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ => ManyAsync(
            Filed("CVS: prescription for Bactrim is ready", now.AddHours(-1)),
            Filed("Vet visit for Milo", now.AddHours(-2))));

        await this.Subject(now).TickAsync(CancellationToken.None);
        await this.Subject(now.AddMinutes(10)).TickAsync(CancellationToken.None);

        await this.channel.Received(1).SendAsync(
            Arg.Is<OutboundContent>(content => content.Provenance == ContentProvenance.ProfileDerived
                && content.Text.Contains("Bactrim can raise INR", StringComparison.Ordinal)
                && content.Text.Contains("forwarded mail", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task On_A_First_Run_Old_Mail_Should_Not_Be_Swept_Up()
    {
        this.facts.TimelineAsync("mail", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ => ManyAsync(
            Filed("Rx: ibuprofen 800 mg", now.AddDays(-5))));

        await this.Subject(now).TickAsync(CancellationToken.None);

        await this.channel.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }
}
