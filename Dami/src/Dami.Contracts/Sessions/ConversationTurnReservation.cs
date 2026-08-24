namespace Dami.Contracts.Sessions;

/// <summary>The stored turn and whether this call won its first reservation.</summary>
public sealed record ConversationTurnReservation
{
    /// <summary>Creates a reservation result.</summary>
    public ConversationTurnReservation(ConversationTurn turn, bool isNew)
    {
        ArgumentNullException.ThrowIfNull(turn);
        this.Turn = turn;
        this.IsNew = isNew;
    }

    /// <summary>The one durable turn returned to every exact retry.</summary>
    public ConversationTurn Turn { get; }

    /// <summary>Whether this call created the durable request.</summary>
    public bool IsNew { get; }
}
