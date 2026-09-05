namespace Dami.Gui;

/// <summary>Persistent application-level activity and system feedback.</summary>
public sealed record GlobalStatus(string Label, string Message, bool IsBusy)
{
    /// <summary>Idle state.</summary>
    public static GlobalStatus Ready { get; } = new("READY", "Dami is ready.", false);

    /// <summary>An accepted action that is still running.</summary>
    public static GlobalStatus Working(string message) => new("WORKING", message, true);

    /// <summary>A completed action.</summary>
    public static GlobalStatus Success(string message) => new("DONE", message, false);

    /// <summary>An action or system failure with its actual reason.</summary>
    public static GlobalStatus Failure(string message) => new("ERROR", message, false);
}
