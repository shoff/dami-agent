namespace Dami.Gui;

/// <summary>A direct task draft with an identity retained across failed submissions.</summary>
public sealed record TaskBoardEntry(
    Guid BoardId, Guid? ParentTaskId, Guid TaskId, string Title,
    string Description, IReadOnlyList<string> Criteria);
