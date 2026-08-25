using Dami.Contracts.Context;
using Dami.Contracts.Models;
using Dami.Contracts.TaskBoard;

namespace Dami.Core.TaskBoard;

/// <summary>Routes Dami-authored planning while enforcing local-only before policy.</summary>
public sealed class DamiFeaturePlanner : IFeaturePlanner
{
    private readonly IModelRouter router;
    private readonly IFeaturePlanner localPlanner;
    private readonly IFeaturePlanner frontierPlanner;

    /// <summary>Creates Dami's composite planner.</summary>
    public DamiFeaturePlanner(
        IModelRouter router,
        IFeaturePlanner localPlanner,
        IFeaturePlanner frontierPlanner)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(localPlanner);
        ArgumentNullException.ThrowIfNull(frontierPlanner);
        if (localPlanner.Kind != FeaturePlannerKind.Local
            || frontierPlanner.Kind != FeaturePlannerKind.Frontier)
        {
            throw new ArgumentException("Dami requires one local and one frontier planner.");
        }

        this.router = router;
        this.localPlanner = localPlanner;
        this.frontierPlanner = frontierPlanner;
    }

    /// <inheritdoc />
    public FeaturePlannerKind Kind => FeaturePlannerKind.Dami;

    /// <inheritdoc />
    public Task<FeaturePlanProposal> PlanAsync(
        FeaturePlanningRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Privacy == PrivacyClass.LocalOnly)
        {
            return this.localPlanner.PlanAsync(request, cancellationToken);
        }

        var route = this.router.Route("feature-planning", request.Privacy);
        var planner = route.Tier == ModelTier.Frontier
            ? this.frontierPlanner
            : this.localPlanner;
        return planner.PlanAsync(request, cancellationToken);
    }
}
