using Dami.Contracts.Models;
using Dami.Contracts.Proactive;
using Dami.Host.Proactive;
using Dami.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dami.Proactive.Tests;

/// <summary>The wiring itself, which nothing else exercises.</summary>
/// <remarks>
/// Every other test constructs these services directly — that is what makes them testable
/// — so until this existed the composition root had no coverage at all. On 2026-08-29
/// NetworkCollectorService gained a LanScanner dependency that nothing registered: it
/// built clean, passed the entire suite, deployed successfully, and then aborted at
/// startup, discovered only by systemd restarting in a loop.
///
/// Resolving is the whole assertion. It never opens a connection or contacts a sidecar —
/// registration builds objects, it does not use them — so this stays a unit test while
/// covering the exact failure that got past everything else.
/// </remarks>
public sealed class ProactiveCompositionTests
{
    private static ServiceProvider Build(params KeyValuePair<string, string?>[] settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection()
            .AddLogging()
            .AddDamiProactiveTier(configuration, "Host=127.0.0.1;Database=dami-test;Username=none")
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = false });
    }

    [Fact]
    public void Every_Proactive_Service_Should_Resolve()
    {
        using var provider = Build();

        var services = provider.GetServices<IProactiveService>().ToList();

        Assert.NotEmpty(services);
        Assert.All(services, service => Assert.False(string.IsNullOrWhiteSpace(service.ServiceName)));
    }

    [Fact]
    public void Every_Proactive_Service_Should_Have_A_Distinct_Name()
    {
        // The scheduler leases, records and reports by name; two services sharing one
        // would silently take each other's turn.
        using var provider = Build();

        var names = provider.GetServices<IProactiveService>().Select(item => item.ServiceName).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_Registered_Services_Should_Include_The_Whole_Tier()
    {
        // Named explicitly so deleting a registration is a failing test rather than a
        // service that quietly stops running.
        using var provider = Build();

        var names = provider.GetServices<IProactiveService>()
            .Select(item => item.ServiceName).ToHashSet(StringComparer.Ordinal);

        Assert.Superset(
            new HashSet<string>(StringComparer.Ordinal)
            {
                "interest-scout", "pushback-audit", "health-collector", "network-collector",
                "civic-collector", "civic-agenda", "curator", "codebase-audit", "reflection",
                "media-librarian", "embedder", "repo-hygiene", "gallery-curator", "opportunity-scout", "fitness-review",
                "signal-nudge", "correlation-card", "inr-cadence",
            },
            names);
    }

    [Fact]
    public void Disabled_DailyPortrait_Should_Not_Be_Scheduled()
    {
        using var provider = Build();

        var services = provider.GetServices<IProactiveService>();

        Assert.DoesNotContain(services, service => service.ServiceName == "daily-portrait");
    }

    [Fact]
    public void Enabled_DailyPortrait_Should_Draw_On_The_Subscription_Not_A_Key()
    {
        // ADR-0029. The keyed OpenAI door was wired here and never configured, so the
        // portrait pass sat enabled-in-theory for four days without producing a picture.
        using var provider = Build(new KeyValuePair<string, string?>("DailyPortrait:Enabled", "true"));

        Assert.Contains(
            provider.GetServices<IProactiveService>(), service => service.ServiceName == "daily-portrait");
        Assert.IsType<CodexSubscriptionImageGenerator>(provider.GetRequiredService<IImageGenerator>());
    }

    [Fact]
    public void Enabled_DailyPortrait_Should_Draw_On_Gemini_When_Selected()
    {
        // ADR-0035. The provider is a configuration choice; the default stays the
        // subscription so nothing changes until Images:Provider is set.
        using var provider = Build(
            new KeyValuePair<string, string?>("DailyPortrait:Enabled", "true"),
            new KeyValuePair<string, string?>("Images:Provider", "Gemini"));

        Assert.IsType<GeminiImageGenerator>(provider.GetRequiredService<IImageGenerator>());
    }

    [Fact]
    public void Enabled_DailyPortrait_Should_Draw_On_Codex_With_Gemini_Behind_It_When_A_Backup_Is_Named()
    {
        // Steve, 2026-09-16: Codex stays primary; a refused or failed picture goes to Gemini.
        using var provider = Build(
            new KeyValuePair<string, string?>("DailyPortrait:Enabled", "true"),
            new KeyValuePair<string, string?>("Images:Backup", "Gemini"));

        Assert.IsType<FallbackImageGenerator>(provider.GetRequiredService<IImageGenerator>());
    }

    [Fact]
    public void A_Backup_Equal_To_The_Primary_Should_Register_The_Primary_Alone()
    {
        // Falling back to the door that just failed would bill twice for the same nothing.
        using var provider = Build(
            new KeyValuePair<string, string?>("DailyPortrait:Enabled", "true"),
            new KeyValuePair<string, string?>("Images:Backup", "Codex"));

        Assert.IsType<CodexSubscriptionImageGenerator>(provider.GetRequiredService<IImageGenerator>());
    }

    [Fact]
    public void Enabled_DailyPortrait_Should_Draw_On_OpenAi_When_Selected()
    {
        using var provider = Build(
            new KeyValuePair<string, string?>("DailyPortrait:Enabled", "true"),
            new KeyValuePair<string, string?>("Images:Provider", "OpenAi"));

        Assert.IsType<OpenAiImageGenerator>(provider.GetRequiredService<IImageGenerator>());
    }

    [Fact]
    public void The_Scheduler_And_Runner_Should_Resolve()
    {
        using var provider = Build();

        Assert.NotNull(provider.GetRequiredService<ProactiveScheduler>());
        Assert.NotNull(provider.GetRequiredService<ProactivePassRunner>());
    }
}
