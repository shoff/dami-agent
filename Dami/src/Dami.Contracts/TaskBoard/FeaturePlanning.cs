namespace Dami.Contracts.TaskBoard;

using Dami.Contracts.Events;
using Dami.Contracts.Context;

/// <summary>Which agent/model boundary creates a feature plan.</summary>
public enum FeaturePlannerKind
{
    /// <summary>The private loopback model.</summary>
    Local,

    /// <summary>An approved egress-capable frontier model.</summary>
    Frontier,

    /// <summary>Dami's own agent workflow.</summary>
    Dami,
}

/// <summary>A feature request entering the planning workflow.</summary>
public sealed record FeaturePlanningRequest(
    Guid RequestId,
    string FeatureRequest,
    TaskActor RequestedBy,
    DateTimeOffset RequestedAt,
    FeaturePlannerKind Planner,
    PrivacyClass Privacy,
    ExecutionOrigin Origin);

/// <summary>A provider-neutral task proposed by a planner.</summary>
public sealed record PlannedTask(
    string Key,
    string Title,
    string Description,
    TaskPriority Priority,
    int Position,
    TaskOrdering SubTaskOrdering,
    IReadOnlyList<string> PrerequisiteKeys,
    IReadOnlyList<string> AcceptanceCriteria,
    IReadOnlyList<PlannedTask> SubTasks);

/// <summary>A planner's complete structured response.</summary>
public sealed record FeaturePlanProposal(
    string Title,
    string Plan,
    TaskOrdering RootOrdering,
    IReadOnlyList<PlannedTask> Tasks);

/// <summary>Creates structured plans without owning persistence.</summary>
public interface IFeaturePlanner
{
    /// <summary>The planner route this implementation serves.</summary>
    FeaturePlannerKind Kind { get; }

    /// <summary>Creates a complete proposal for one feature request.</summary>
    Task<FeaturePlanProposal> PlanAsync(
        FeaturePlanningRequest request,
        CancellationToken cancellationToken);
}
