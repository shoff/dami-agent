namespace Dami.Contracts.TaskBoard;

/// <summary>A stable human or agent identity recorded on task-board mutations.</summary>
public sealed record TaskActor
{
    /// <summary>Creates an actor.</summary>
    public TaskActor(string actorId, TaskActorKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown actor kind.");
        }

        this.ActorId = actorId;
        this.Kind = kind;
    }

    /// <summary>Stable identifier meaningful to the caller.</summary>
    public string ActorId { get; }

    /// <summary>Whether this is a human or an agent.</summary>
    public TaskActorKind Kind { get; }
}
