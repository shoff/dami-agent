namespace Dami.Core.Sessions;

/// <summary>Builds bounded recent conversation for a session turn.</summary>
public interface IConversationWindowBuilder
{
    /// <summary>Reads and budgets recent completed exchanges.</summary>
    Task<ConversationWindow> BuildAsync(Guid sessionId, CancellationToken cancellationToken);
}
