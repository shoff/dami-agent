namespace Dami.Contracts.Sessions;

/// <summary>One durably reserved or completed turn in a conversation.</summary>
public sealed record ConversationTurn
{
    /// <summary>Creates a stored turn.</summary>
    public ConversationTurn(
        long sequence,
        ConversationTurnRequest request,
        Guid traceId,
        ConversationTurnState state,
        string? response = null,
        DateTimeOffset? completedAt = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (sequence <= 0 || traceId == Guid.Empty)
        {
            throw new ArgumentException("Stored turns require a positive sequence and non-empty trace.");
        }

        ValidateTerminalState(state, response, completedAt);
        if (completedAt < request.RequestedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedAt), completedAt, "A turn cannot complete before it was requested.");
        }

        this.Sequence = sequence;
        this.Request = request;
        this.TraceId = traceId;
        this.State = state;
        this.Response = response;
        this.CompletedAt = completedAt;
    }

    /// <summary>Monotonic database order.</summary>
    public long Sequence { get; }

    /// <summary>The idempotent user request.</summary>
    public ConversationTurnRequest Request { get; }

    /// <summary>The execution trace assigned on first reservation.</summary>
    public Guid TraceId { get; }

    /// <summary>Current durable execution state.</summary>
    public ConversationTurnState State { get; }

    /// <summary>The complete assistant response, only for Completed turns.</summary>
    public string? Response { get; }

    /// <summary>When a terminal state was persisted.</summary>
    public DateTimeOffset? CompletedAt { get; }

    private static void ValidateTerminalState(
        ConversationTurnState state,
        string? response,
        DateTimeOffset? completedAt)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown turn state.");
        }

        var valid = state switch
        {
            ConversationTurnState.Running => response is null && completedAt is null,
            ConversationTurnState.Completed => response is not null && completedAt is not null,
            _ => response is null && completedAt is not null,
        };
        if (!valid)
        {
            throw new ArgumentException("Turn response and completion time do not match its state.");
        }
    }
}
