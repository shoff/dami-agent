using Dami.Contracts.Sessions;

namespace Dami.Core.Sessions;

/// <summary>A bounded recent conversation window and its prompt cost.</summary>
public sealed record ConversationWindow
{
    /// <summary>No prior conversation.</summary>
    public static ConversationWindow Empty { get; } = new([], 0);

    /// <summary>Creates a window.</summary>
    public ConversationWindow(IReadOnlyList<ConversationTurn> turns, int estimatedTokens)
    {
        ArgumentNullException.ThrowIfNull(turns);
        ArgumentOutOfRangeException.ThrowIfNegative(estimatedTokens);
        if (turns.Any(turn => turn.State != ConversationTurnState.Completed))
        {
            throw new ArgumentException(
                "A conversation window can contain only completed turns.", nameof(turns));
        }

        this.Turns = Array.AsReadOnly(turns.ToArray());
        this.EstimatedTokens = estimatedTokens;
    }

    /// <summary>Completed turns in oldest-to-newest conversation order.</summary>
    public IReadOnlyList<ConversationTurn> Turns { get; }

    /// <summary>Conservative prompt-token estimate for the exchanges.</summary>
    public int EstimatedTokens { get; }
}
