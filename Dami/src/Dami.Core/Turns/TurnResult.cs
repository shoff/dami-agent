using Dami.Contracts.Context;
using Dami.Contracts.Models;

namespace Dami.Core.Turns;

/// <summary>What a turn produced: the answer, and the accounting behind it.</summary>
public sealed record TurnResult
{
    /// <summary>Creates a result.</summary>
    public TurnResult(Guid traceId, string answer, AssembledContext context, ModelRoute route)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(route);

        this.TraceId = traceId;
        this.Answer = answer;
        this.Context = context;
        this.Route = route;
    }

    /// <summary>The trace the whole turn is replayable from.</summary>
    public Guid TraceId { get; }

    /// <summary>The model's answer.</summary>
    public string Answer { get; }

    /// <summary>What entered the prompt, with provenance and token cost.</summary>
    public AssembledContext Context { get; }

    /// <summary>Where it ran, and why.</summary>
    public ModelRoute Route { get; }
}
