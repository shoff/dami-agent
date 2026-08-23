using Dami.Contracts.Events;
using Dami.Contracts.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dami.Persistence.Tests;

/// <summary>The composition-root registration.</summary>
public sealed class ServiceCollectionExtensionsTests
{
    private const string CONNECTION = "Host=127.0.0.1;Database=dami-data;Username=dami_app";

    [Fact]
    public void AddDamiPersistence_Should_Reject_A_Null_Connection_String()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ServiceCollection().AddDamiPersistence(null!));
    }

    [Theory]
    [InlineData(typeof(IExecutionEventStore))]
    [InlineData(typeof(IObservationCorpus))]
    [InlineData(typeof(IConclusionLedger))]
    [InlineData(typeof(IPushbackLedger))]
    public void AddDamiPersistence_Should_Resolve_Every_Store(Type contract)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddDamiPersistence(CONNECTION);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService(contract));
    }

    [Fact]
    public void AddDamiPersistence_Should_Register_Nothing_That_Reaches_The_Network_Beyond_Postgres()
    {
        var services = new ServiceCollection();
        services.AddDamiPersistence(CONNECTION);

        var offenders = services
            .Where(descriptor => descriptor.ServiceType.FullName is { } name
                && (name.StartsWith("System.Net.Http", StringComparison.Ordinal)
                    || name.Contains("EgressClient", StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(offenders);
    }
}
