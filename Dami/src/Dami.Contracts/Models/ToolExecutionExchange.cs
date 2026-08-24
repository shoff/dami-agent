using Dami.Contracts.Capabilities;

namespace Dami.Contracts.Models;

/// <summary>One model-requested tool call and its evidence-backed result.</summary>
public sealed class ToolExecutionExchange
{
    /// <summary>Creates a completed tool exchange.</summary>
    public ToolExecutionExchange(
        string callId,
        CapabilityInvocation invocation,
        CapabilityExecutionResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callId);
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(result);
        this.CallId = callId;
        this.Invocation = invocation;
        this.Result = result;
    }

    /// <summary>Gets the provider's correlation identifier.</summary>
    public string CallId { get; }

    /// <summary>Gets the source-neutral capability invocation.</summary>
    public CapabilityInvocation Invocation { get; }

    /// <summary>Gets the evidence-backed successful result.</summary>
    public CapabilityExecutionResult Result { get; }
}
