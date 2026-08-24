using Dami.Contracts.Capabilities;

namespace Dami.Capabilities;

/// <summary>Dispatches an invocation to exactly one source without exposing its kind.</summary>
public sealed class CapabilityExecutorDispatcher : ICapabilityExecutor
{
    private readonly IReadOnlyList<ICapabilityExecutionSource> sources;

    /// <summary>Creates a dispatcher over a fixed execution-source snapshot.</summary>
    public CapabilityExecutorDispatcher(IEnumerable<ICapabilityExecutionSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ICapabilityExecutionSource[] snapshot = sources.ToArray();
        if (Array.Exists(snapshot, static source => source is null))
        {
            throw new ArgumentException("Execution sources cannot contain null.", nameof(sources));
        }

        this.sources = snapshot;
    }

    /// <inheritdoc />
    public Task<CapabilityExecutionResult> ExecuteAsync(
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ICapabilityExecutionSource? selected = null;
        foreach (ICapabilityExecutionSource source in this.sources)
        {
            if (!source.Owns(request.Invocation.CapabilityId))
            {
                continue;
            }

            selected = selected is null
                ? source
                : throw new InvalidOperationException(
                    $"Multiple execution sources own capability '{request.Invocation.CapabilityId}'.");
        }

        return selected?.ExecuteAsync(request, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"No execution source owns capability '{request.Invocation.CapabilityId}'.");
    }
}
