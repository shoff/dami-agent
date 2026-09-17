using Dami.Contracts.TaskBoard;

namespace Dami.Gui;

/// <summary>A prerequisite's stable identity, readable name and observed status.</summary>
public sealed record TaskBoardDependency(Guid TaskId, string Title, TaskBoardStatus Status);
