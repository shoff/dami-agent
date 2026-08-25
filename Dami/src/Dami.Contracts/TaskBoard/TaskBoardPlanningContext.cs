using Dami.Contracts.Context;
using Dami.Contracts.Events;

namespace Dami.Contracts.TaskBoard;

/// <summary>The model and disclosure boundary that produced a board's initial plan.</summary>
public sealed record TaskBoardPlanningContext
{
    /// <summary>Creates persisted planning provenance.</summary>
    public TaskBoardPlanningContext(
        FeaturePlannerKind planner,
        PrivacyClass privacy,
        ExecutionOrigin origin)
    {
        if (!Enum.IsDefined(planner))
        {
            throw new ArgumentOutOfRangeException(nameof(planner), planner, "Unknown planner.");
        }

        if (!Enum.IsDefined(privacy))
        {
            throw new ArgumentOutOfRangeException(nameof(privacy), privacy, "Unknown privacy.");
        }

        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), origin, "Unknown origin.");
        }

        this.Planner = planner;
        this.Privacy = privacy;
        this.Origin = origin;
    }

    /// <summary>The requested planning implementation.</summary>
    public FeaturePlannerKind Planner { get; }

    /// <summary>The disclosure classification enforced while planning.</summary>
    public PrivacyClass Privacy { get; }

    /// <summary>The execution origin that requested planning.</summary>
    public ExecutionOrigin Origin { get; }
}
