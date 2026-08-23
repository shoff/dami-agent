using Dami.Contracts.Context;
using Dami.Contracts.Models;
using Dami.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Core.Tests.Context;

/// <summary>D-012 as a routing input: the rule no configuration can override.</summary>
public sealed class ModelRouterTests
{
    [Fact]
    public void Route_Should_Keep_LocalOnly_Work_Local_Even_With_Frontier_Enabled()
    {
        var router = CreateRouter(frontierEnabled: true);

        var route = router.Route("synthesis", PrivacyClass.LocalOnly);

        Assert.Equal(ModelTier.Local, route.Tier);
    }

    [Fact]
    public void Route_Should_Send_Egressable_Synthesis_To_The_Frontier()
    {
        var router = CreateRouter(frontierEnabled: true);

        var route = router.Route("synthesis", PrivacyClass.Egressable);

        Assert.Equal(ModelTier.Frontier, route.Tier);
    }

    [Fact]
    public void Route_Should_Keep_Simple_Work_Local_Regardless_Of_Privacy()
    {
        var router = CreateRouter(frontierEnabled: true);

        var route = router.Route("classification", PrivacyClass.Egressable);

        Assert.Equal(ModelTier.Local, route.Tier);
    }

    [Fact]
    public void Route_Should_Degrade_To_Local_When_No_Frontier_Exists()
    {
        var router = CreateRouter(frontierEnabled: false);

        var route = router.Route("synthesis", PrivacyClass.Egressable);

        Assert.Equal(ModelTier.Local, route.Tier);
    }

    [Fact]
    public void Route_Should_Say_Why()
    {
        var router = CreateRouter(frontierEnabled: true);

        var route = router.Route("synthesis", PrivacyClass.LocalOnly);

        Assert.Contains("D-012", route.Reason, StringComparison.Ordinal);
    }

    private static ModelRouter CreateRouter(bool frontierEnabled)
    {
        return new ModelRouter(
            Options.Create(new RoutingOptions { FrontierEnabled = frontierEnabled }),
            NullLogger<ModelRouter>.Instance);
    }
}
