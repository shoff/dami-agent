namespace Dami.Contracts.TaskBoard;

/// <summary>A testable condition that must be met before a task is done.</summary>
public sealed record AcceptanceCriterionDraft
{
    /// <summary>Creates a criterion for a new plan.</summary>
    public AcceptanceCriterionDraft(Guid criterionId, string description, int position)
    {
        if (criterionId == Guid.Empty)
        {
            throw new ArgumentException("A criterion id cannot be empty.", nameof(criterionId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        this.CriterionId = criterionId;
        this.Description = description;
        this.Position = position;
    }

    /// <summary>Stable criterion identifier.</summary>
    public Guid CriterionId { get; }

    /// <summary>Objectively evaluable completion condition.</summary>
    public string Description { get; }

    /// <summary>Stable display position within its task.</summary>
    public int Position { get; }
}

/// <summary>A persisted acceptance criterion and its current result.</summary>
public sealed record AcceptanceCriterion(
    Guid CriterionId,
    string Description,
    int Position,
    bool IsSatisfied,
    TaskActor? SatisfiedBy,
    DateTimeOffset? SatisfiedAt);
