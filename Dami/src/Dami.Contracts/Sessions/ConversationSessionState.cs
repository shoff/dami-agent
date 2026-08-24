namespace Dami.Contracts.Sessions;

/// <summary>Whether a conversation accepts turns or has been deliberately interrupted.</summary>
public enum ConversationSessionState
{
    /// <summary>The session accepts new turns.</summary>
    Active = 0,

    /// <summary>The session is paused until explicitly resumed.</summary>
    Interrupted = 1,
}
