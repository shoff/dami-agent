using Dami.Contracts.Capabilities;

namespace Dami.Contracts.Models;

/// <summary>One model-requested tool call and what came back from it.</summary>
/// <remarks>
/// An exchange models both outcomes on purpose. A tool that throws is not an exception to
/// the conversation, it is a turn in it: the model asked for something, it did not work,
/// and the model is entitled to know why and try again within its remaining call budget.
/// Before this carried failures, one bad argument from a small model — a file path of
/// literally <c>"path"</c> — killed the whole turn.
///
/// <see cref="CapabilityExecutionResult"/> stays what it says it is: a *successful*
/// output backed by evidence. A failure is never dressed up as one, which is why the
/// result is null on that path and <see cref="Content"/> exists to give providers the one
/// thing they actually need.
/// </remarks>
public sealed class ToolExecutionExchange
{
    /// <summary>Creates a completed, successful tool exchange.</summary>
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

    private ToolExecutionExchange(string callId, CapabilityInvocation invocation, string failure)
    {
        this.CallId = callId;
        this.Invocation = invocation;
        this.Failure = failure;
    }

    /// <summary>
    /// Creates an exchange for a tool that failed, carrying the reason back to the model.
    /// </summary>
    public static ToolExecutionExchange Failed(
        string callId,
        CapabilityInvocation invocation,
        string failure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callId);
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(failure);
        return new ToolExecutionExchange(callId, invocation, failure);
    }

    /// <summary>Gets the provider's correlation identifier.</summary>
    public string CallId { get; }

    /// <summary>Gets the source-neutral capability invocation.</summary>
    public CapabilityInvocation Invocation { get; }

    /// <summary>Gets the evidence-backed successful result, or null when the tool failed.</summary>
    public CapabilityExecutionResult? Result { get; }

    /// <summary>Gets why the tool failed, or null when it succeeded.</summary>
    public string? Failure { get; }

    /// <summary>Whether the tool call succeeded.</summary>
    public bool Succeeded => this.Result is not null;

    /// <summary>
    /// What to hand back to the model as this call's result — the tool's output, or an
    /// error it can act on. Providers want this and should not branch on the outcome.
    /// </summary>
    public string Content => this.Result?.Output ?? $"The tool failed: {this.Failure}";
}
