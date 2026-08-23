using Dami.Contracts.Context;
using Dami.Contracts.Models;

namespace Dami.Core.Turns;

/// <summary>A turn in flight: the accounting up front, the answer as it arrives.</summary>
public sealed record TurnStream
{
    /// <summary>Creates a stream.</summary>
    public TurnStream(
        Guid traceId,
        AssembledContext context,
        ModelRoute route,
        IAsyncEnumerable<string> tokens)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(tokens);

        this.TraceId = traceId;
        this.Context = context;
        this.Route = route;
        this.Tokens = tokens;
    }

    /// <summary>The trace the whole turn is replayable from.</summary>
    public Guid TraceId { get; }

    /// <summary>What entered the prompt, with provenance and token cost.</summary>
    public AssembledContext Context { get; }

    /// <summary>Where it runs, and why.</summary>
    public ModelRoute Route { get; }

    /// <summary>The answer, fragment by fragment. Drain it to complete the turn.</summary>
    public IAsyncEnumerable<string> Tokens { get; }
}
