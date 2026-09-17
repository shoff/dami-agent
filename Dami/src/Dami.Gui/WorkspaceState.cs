namespace Dami.Gui;

/// <summary>Local navigation and focus preferences, independent of runtime requests.</summary>
public sealed class WorkspaceState
{
    /// <summary>The workspace page to present.</summary>
    public int PageIndex { get; private set; }

    /// <summary>Whether attention panels are hidden to give chat the full width.</summary>
    public bool IsFocusMode { get; private set; }

    /// <summary>Applies a navigation or view action without sending a message.</summary>
    public void Activate(WorkspaceCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        this.PageIndex = command.PageIndex;
        if (command.Id == "focus")
        {
            this.IsFocusMode = !this.IsFocusMode;
        }
    }
}
