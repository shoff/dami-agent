using Avalonia.Input;

namespace Dami.Gui;

/// <summary>A discoverable desktop action, optionally opening a workspace page.</summary>
public sealed record WorkspaceCommand(string Id, string Title, string Description, string Shortcut, int PageIndex);

/// <summary>Navigation and local view actions available without contacting the runtime.</summary>
public static class WorkspaceCommands
{
    private static readonly WorkspaceCommand[] commands =
    [
        new("chat", "Conversation", "Ask, explore ideas, or create something together", "Ctrl+1", 0),
        new("plan", "Plan & tasks", "Browse boards, plan features and claim work", "Ctrl+2", 1),
        new("activity", "Activity", "Inspect live events, traces and runtime status", "Ctrl+3", 2),
        new("workers", "Workers", "Explore background services, runs and schedules", "Ctrl+4", 3),
        new("health", "Health", "Review fitness, weight, training and progress", "Ctrl+5", 4),
        new("gallery", "Gallery", "Browse images and create a Dami portrait", "Ctrl+6", 5),
        new("network", "Network", "Inspect devices, sweeps and connections", "Ctrl+7", 6),
        new("compose", "Write a message", "Jump to the conversation composer", "Ctrl+L", 0),
        new("focus", "Toggle focus mode", "Hide or show attention and beliefs beside chat", "", 0),
        new("research", "Research", "Explore saved findings, sources and research progress", "Ctrl+8", 7),
    ];

    /// <summary>Matches every search word against the action's title and description.</summary>
    public static IReadOnlyList<WorkspaceCommand> Search(string? query)
    {
        var words = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matches = new List<WorkspaceCommand>();
        foreach (var command in commands)
        {
            var text = command.Title + " " + command.Description;
            if (words.All(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)))
            {
                matches.Add(command);
            }
        }

        return matches;
    }
    /// <summary>Resolves exact Ctrl shortcuts without consuming ordinary typing.</summary>
    public static WorkspaceCommand? FromKey(Key key, KeyModifiers modifiers)
    {
        if (modifiers != KeyModifiers.Control)
        {
            return null;
        }

        if (key is >= Key.D1 and <= Key.D7)
        {
            return commands[key - Key.D1];
        }

        return key switch { Key.L => commands[7], Key.D8 => commands[9], _ => null };
    }
}
