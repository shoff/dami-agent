using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Dami.Contracts.TaskBoard;
using Dami.Core.TaskBoard;
using Xunit;

namespace Dami.Core.Tests.TaskBoard;

public sealed class DamiFeaturePlannerTests
{
    [Fact]
    public async Task PlanAsync_Should_Force_LocalOnly_To_Local_When_The_Router_Lies()
    {
        var local = new CountingPlanner(FeaturePlannerKind.Local);
        var frontier = new CountingPlanner(FeaturePlannerKind.Frontier);
        var router = new StubRouter(ModelTier.Frontier);
        var planner = new DamiFeaturePlanner(router, local, frontier);
        var request = new FeaturePlanningRequest(
            Guid.NewGuid(), "private plan", new TaskActor("dami", TaskActorKind.Agent),
            new DateTimeOffset(2026, 8, 24, 22, 10, 0, TimeSpan.Zero),
            FeaturePlannerKind.Dami, PrivacyClass.LocalOnly, ExecutionOrigin.ReactiveTrigger);

        await planner.PlanAsync(request, CancellationToken.None);

        Assert.Equal((1, 0, 0), (local.Calls, frontier.Calls, router.Calls));
    }

    private sealed class StubRouter : IModelRouter
    {
        private readonly ModelTier tier;

        internal StubRouter(ModelTier tier)
        {
            this.tier = tier;
        }

        internal int Calls { get; private set; }

        public ModelRoute Route(string workKind, PrivacyClass privacy)
        {
            this.Calls++;
            return new ModelRoute(this.tier, privacy, "test route");
        }
    }

    private sealed class CountingPlanner : IFeaturePlanner
    {
        internal CountingPlanner(FeaturePlannerKind kind)
        {
            this.Kind = kind;
        }

        public FeaturePlannerKind Kind { get; }

        internal int Calls { get; private set; }

        public Task<FeaturePlanProposal> PlanAsync(
            FeaturePlanningRequest request,
            CancellationToken cancellationToken)
        {
            this.Calls++;
            return Task.FromResult(new FeaturePlanProposal(
                "title", "plan", TaskOrdering.Ordered, []));
        }
    }
}
