using Dami.Contracts.TaskBoard;

namespace Dami.Core.TaskBoard;

/// <summary>Turns one planner proposal into one atomic persistent board draft.</summary>
public sealed class FeaturePlanningService
{
    private readonly IReadOnlyDictionary<FeaturePlannerKind, IFeaturePlanner> planners;
    private readonly ITaskBoardStore store;

    /// <summary>Creates the planning application service.</summary>
    public FeaturePlanningService(
        IEnumerable<IFeaturePlanner> planners,
        ITaskBoardStore store)
    {
        ArgumentNullException.ThrowIfNull(planners);
        ArgumentNullException.ThrowIfNull(store);
        this.planners = planners.ToDictionary(planner => planner.Kind);
        this.store = store;
    }

    /// <summary>Plans and persists one request.</summary>
    public async Task<Guid> PlanAsync(
        FeaturePlanningRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        var existing = await this.store.FindAsync(request.RequestId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            EnsureSameRequest(request, existing);
            return existing.BoardId;
        }

        if (!this.planners.TryGetValue(request.Planner, out var planner))
        {
            throw new InvalidOperationException(
                $"No feature planner is registered for '{request.Planner}'.");
        }

        var proposal = await planner.PlanAsync(request, cancellationToken)
            .ConfigureAwait(false);
        var draft = FeaturePlanMapper.Map(request, proposal);
        await this.store.CreateAsync(draft, cancellationToken).ConfigureAwait(false);
        return draft.BoardId;
    }

    private static void EnsureSameRequest(
        FeaturePlanningRequest request,
        TaskBoardSnapshot existing)
    {
        if (!string.Equals(
                request.FeatureRequest, existing.FeatureRequest, StringComparison.Ordinal)
            || request.RequestedBy != existing.CreatedBy
            || request.RequestedAt.ToUniversalTime().Ticks / 10
                != existing.CreatedAt.ToUniversalTime().Ticks / 10
            || existing.PlanningContext != new TaskBoardPlanningContext(
                request.Planner, request.Privacy, request.Origin))
        {
            throw new InvalidOperationException(
                $"Planning request '{request.RequestId}' already has different content.");
        }
    }

    private static void ValidateRequest(FeaturePlanningRequest request)
    {
        if (request.RequestId == Guid.Empty)
        {
            throw new ArgumentException("A planning request id cannot be empty.", nameof(request));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(request.FeatureRequest);
        ArgumentNullException.ThrowIfNull(request.RequestedBy);
        if (!Enum.IsDefined(request.Planner))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request), request.Planner, "Unknown feature planner kind.");
        }
    }
}
