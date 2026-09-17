using Dami.Contracts.Gateways;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Contracts.Sessions;
using Dami.Core.Frontier;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace Dami.Host.Discord.Tests;

/// <summary>
/// That the gateway's dependency graph can actually be built.
/// </summary>
/// <remarks>
/// The failure this exists for is recorded: a service gained a dependency nobody
/// registered, the change built clean, passed the whole suite, deployed, and aborted at
/// startup in a restart loop. Every other test here constructs the worker directly, which
/// is exactly what makes them blind to it — ADR-0026 added four dependencies at once.
/// </remarks>
public sealed class DiscordCompositionTests
{
    private static ServiceProvider Compose()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Discord:Enabled"] = "true",
                ["Discord:Token"] = "a-token",
                ["Discord:OwnerUserId"] = "347544641295613953",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);

        // The runtime's own registrations, stubbed: this asserts the gateway's wiring,
        // not the whole host's.
        AddRuntimeStubs(services);
        services.AddDamiDiscordGateway(configuration);
        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>
    /// The runtime's own registrations, stubbed. No ITracedTurnRunner on purpose: the
    /// gateway has no local model to answer with, and this proves it never needs one
    /// (ADR-0028).
    /// </summary>
    private static void AddRuntimeStubs(ServiceCollection services)
    {
        services.AddSingleton(Substitute.For<IGatewayAuthority>());
        services.AddSingleton(Substitute.For<IAugmentedTurn>());
        services.AddSingleton(Substitute.For<IVisionClient>());
        services.AddSingleton(Substitute.For<IImageGenerator>());
        services.AddSingleton(Substitute.For<IPortraitGenerator>());
        services.AddSingleton(Substitute.For<IFrontierRecall>());
        services.AddSingleton(Substitute.For<IFrontierRemember>());
        services.AddSingleton(Substitute.For<IFrontierLesson>());
        services.AddSingleton(Substitute.For<IStandingLessons>());
        services.AddSingleton(Substitute.For<IFrontierScheduling>());
        services.AddSingleton(Substitute.For<Dami.Core.Gallery.IGallerySearch>());
        services.AddSingleton(Substitute.For<Dami.Contracts.Gallery.IGalleryPictures>());
        services.AddSingleton(Substitute.For<IFrontierFitness>());
        services.AddSingleton(Substitute.For<IFrontierResearch>());
        services.AddSingleton(Substitute.For<IFrontierToday>());
        services.AddSingleton(Substitute.For<IFrontierCode>());
        services.AddSingleton(Substitute.For<Dami.Contracts.Proactive.ISurfacingQueue>());
        services.AddSingleton<FrontierToolBundle>();
        services.AddSingleton(Substitute.For<IConversationSessionStore>());
        services.AddSingleton(Substitute.For<IConversationTurnStore>());
        services.AddSingleton(Substitute.For<IProactiveRunHistory>());
    }

    [Fact]
    public void Configured_Gateway_Should_Resolve_Its_Worker()
    {
        using var provider = Compose();

        Assert.Contains(
            provider.GetServices<IHostedService>(),
            service => service is DiscordGatewayWorker);
    }

    [Fact]
    public void Configured_Gateway_Should_Offer_Scheduled_Delivery_Back_Into_Discord()
    {
        // Migration 039: a job drafted on Discord comes back as a Discord turn.
        using var provider = Compose();

        Assert.Contains(
            provider.GetServices<Dami.Core.Scheduling.IScheduledPromptDelivery>(),
            delivery => delivery is DiscordScheduledDelivery);
    }

    [Fact]
    public void Configured_Gateway_Should_Resolve_Local_Vision()
    {
        // ADR-0026: without this the gateway starts and every image silently goes unread.
        using var provider = Compose();

        Assert.NotNull(provider.GetRequiredService<DiscordVision>());
    }

    [Fact]
    public void Configured_Gateway_Should_Grant_Exactly_One_Channel()
    {
        // D-012's audit point: what can reach off the host stays countable.
        using var provider = Compose();

        Assert.Single(provider.GetServices<IEgressChannel>());
    }
}
