using Dami.Contracts.Events;

namespace Dami.Contracts.Code;

/// <summary>A question about the code, and the turn it belongs to.</summary>
public sealed record CodeQuestion
{
    /// <summary>Creates a question.</summary>
    public CodeQuestion(string question, Guid traceId, ExecutionOrigin origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        this.Question = question;
        this.TraceId = traceId;
        this.Origin = origin;
    }

    /// <summary>What to explain. Goes to the coding agent; never into an event label.</summary>
    public string Question { get; }

    /// <summary>The turn this belongs to.</summary>
    public Guid TraceId { get; }

    /// <summary>What kind of work asked for it.</summary>
    public ExecutionOrigin Origin { get; }
}
