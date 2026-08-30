using Dami.Contracts.Proactive;
using Dami.Host.Proactive;
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
    private static ServiceProvider Build()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([]).Build();
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
                "media-librarian", "embedder", "repo-hygiene",
            },
            names);
    }

    [Fact]
    public void The_Scheduler_And_Runner_Should_Resolve()
    {
        using var provider = Build();

        Assert.NotNull(provider.GetRequiredService<ProactiveScheduler>());
        Assert.NotNull(provider.GetRequiredService<ProactivePassRunner>());
    }
}
