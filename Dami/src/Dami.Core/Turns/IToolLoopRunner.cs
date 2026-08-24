using Dami.Contracts.Capabilities;
using Dami.Contracts.Context;
using Dami.Contracts.Events;

namespace Dami.Core.Turns;

/// <summary>Runs a bounded model/tool exchange within one turn trace.</summary>
public interface IToolLoopRunner
{
    /// <summary>Runs until the model answers or the configured tool-call bound is reached.</summary>
    Task<string> RunAsync(
        Guid traceId,
        Guid parentSpanId,
        string prompt,
        IReadOnlyList<CapabilityToolSchema> toolSchemas,
        PrivacyClass privacy,
        ExecutionOrigin origin,
        CancellationToken cancellationToken);
}
