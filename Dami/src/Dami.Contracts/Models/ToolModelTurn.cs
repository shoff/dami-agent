using Dami.Contracts.Capabilities;

namespace Dami.Contracts.Models;

/// <summary>One model step: either a final answer or one capability call.</summary>
public sealed class ToolModelTurn
{
    private ToolModelTurn(
        string? answer,
        string? callId,
        CapabilityInvocation? invocation)
    {
        this.Answer = answer;
        this.CallId = callId;
        this.Invocation = invocation;
    }

    /// <summary>Gets the final answer, or null when the model requested a tool.</summary>
    public string? Answer { get; }

    /// <summary>Gets the provider call identifier, or null for a final answer.</summary>
    public string? CallId { get; }

    /// <summary>Gets the requested invocation, or null for a final answer.</summary>
    public CapabilityInvocation? Invocation { get; }

    /// <summary>Creates a final-answer step.</summary>
    public static ToolModelTurn ForAnswer(string answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return new ToolModelTurn(answer, null, null);
    }

    /// <summary>Creates a single-tool-call step.</summary>
    public static ToolModelTurn ForCall(string callId, CapabilityInvocation invocation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callId);
        ArgumentNullException.ThrowIfNull(invocation);
        return new ToolModelTurn(null, callId, invocation);
    }
}
