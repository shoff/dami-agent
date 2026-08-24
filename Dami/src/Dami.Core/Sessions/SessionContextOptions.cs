namespace Dami.Core.Sessions;

/// <summary>Hard bounds for recent conversation included in a turn prompt.</summary>
public sealed class SessionContextOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "SessionContext";

    /// <summary>Maximum completed exchanges considered.</summary>
    public int RecentTurnLimit { get; set; } = 6;

    /// <summary>Maximum estimated tokens contributed by conversation history.</summary>
    public int MaxConversationTokens { get; set; } = 1000;
}
