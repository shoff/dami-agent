using Dami.Contracts.TaskBoard;
using Dami.Core.BoardImport;
using Xunit;

namespace Dami.Core.Tests.BoardImport;

public sealed class TodoBoardRendererTests
{
    private static readonly DateTimeOffset at = new(2026, 8, 25, 20, 0, 0, TimeSpan.Zero);
    private static readonly TaskActor claude = new("claude", TaskActorKind.Agent);

    [Fact]
    public void Render_Should_Write_Every_Status_In_The_Grammar_The_Reader_Accepts()
    {
        var done = Leaf("Q1 Finished", TaskBoardStatus.Done);
        var held = Leaf("Q2 Held", TaskBoardStatus.InProgress, new TaskClaim(claude, at));
        var blocked = Leaf("Q3 Stuck", TaskBoardStatus.Blocked, description: "- [ ] Q3 Stuck\n\nBlocked: waiting on a key");
        var steve = Leaf("Q4 Yours", TaskBoardStatus.Blocked, description: "- [STEVE] Q4 Yours");
        var cancelled = Leaf("Q5 Dropped", TaskBoardStatus.Cancelled);
        var dependent = Leaf("Q6 Later", TaskBoardStatus.Open, prerequisites: [done.TaskId],
            criteria: [new AcceptanceCriterion(Guid.NewGuid(), "acceptance item 3", 0, false, null, null),
                new AcceptanceCriterion(Guid.NewGuid(), "it exists", 1, true, claude, at)]);
        var child = Leaf("Q2a Nested", TaskBoardStatus.Open);
        var board = Board(Root("Q · Quiet", [done, held with { SubTasks = [child] }, blocked, steve, cancelled, dependent]));

        var text = TodoBoardRenderer.Render(board);
        var reread = TodoBoardParser.Parse(text);

        Assert.Contains("- [x] Q1 Finished", text, StringComparison.Ordinal);
        Assert.Contains("- [~ Claude 2026-08-25] Q2 Held", text, StringComparison.Ordinal);
        Assert.Contains("  - [ ] Q2a Nested", text, StringComparison.Ordinal);
        Assert.Contains("- [ ] Q3 Stuck `[BLOCKED: waiting on a key]`", text, StringComparison.Ordinal);
        Assert.Contains("- [STEVE] Q4 Yours", text, StringComparison.Ordinal);
        Assert.Contains("- [-] Q5 Dropped", text, StringComparison.Ordinal);
        Assert.Contains("- [ ] Q6 Later — acceptance item 3 (needs Q1 first)", text, StringComparison.Ordinal);
        Assert.Contains("<!-- criterion [x]: it exists -->", text, StringComparison.Ordinal);
        var section = Assert.Single(reread.Sections);
        Assert.Equal(["Q1", "Q2", "Q3", "Q4", "Q5", "Q6"], section.Entries.Select(entry => entry.Id));
        Assert.Equal([TodoState.Done, TodoState.InProgress, TodoState.Open, TodoState.NeedsSteve, TodoState.Cancelled, TodoState.Open],
            section.Entries.Select(entry => entry.State));
        Assert.Equal("waiting on a key", section.Entries[2].BlockedReason);
        Assert.Equal(("Claude", new DateOnly(2026, 8, 25)), (section.Entries[1].Owner, section.Entries[1].ClaimedOn));
        Assert.Equal("Q1", Assert.Single(section.Entries[5].DependsOnIds));
        Assert.Empty(reread.Anomalies);
    }

    [Fact]
    public void Render_Should_Comment_Out_What_The_Grammar_Cannot_Say_Instead_Of_Inventing_A_Task()
    {
        var noId = Leaf("added without an id", TaskBoardStatus.Open);
        var board = Board(
            Root("Q · Quiet", [noId]),
            Root("Free-form root", [Leaf("R1 Under a keyless root", TaskBoardStatus.Open)]));

        var text = TodoBoardRenderer.Render(board);
        var reread = TodoBoardParser.Parse(text);

        Assert.Contains("<!-- task without an id, not rendered:", text, StringComparison.Ordinal);
        Assert.Contains("<!-- root without a section key, not rendered:", text, StringComparison.Ordinal);
        Assert.Empty(reread.Sections);
    }

    [Fact]
    public void Render_Should_Round_Trip_The_Real_Blueprint()
    {
        var original = TodoBoardMapper.Map(
            TodoBoardParser.Parse(File.ReadAllText(FindBoard())),
            new TodoImportSource("test", "request", "plan"), claude, at);
        var board = Board([.. original.Draft.Tasks.Select(task => AsBoardTask(task, original.Desired))]);

        var text = TodoBoardRenderer.Render(board);
        var reread = TodoBoardMapper.Map(
            TodoBoardParser.Parse(text), new TodoImportSource("test", "request", "plan"), claude, at);

        // An entry with no id (the struck-through `~~G9~~ posture` line) has no identity the
        // grammar can carry back; it is written as a comment, and the file reports it.
        var renderable = original.Desired.Count(task => task.TodoId is not null || task.Depth == 0);
        Assert.Equal(renderable, reread.Desired.Count);
        Assert.Contains("<!-- task without an id, not rendered:", text, StringComparison.Ordinal);
        var before = original.Desired.ToDictionary(task => task.TaskId);
        foreach (var task in reread.Desired)
        {
            Assert.True(before.TryGetValue(task.TaskId, out var was), $"{task.TodoId} gained a new identity");
            Assert.Equal((was.TodoId, was.Depth, was.CriterionIds.Count), (task.TodoId, task.Depth, task.CriterionIds.Count));
        }

        var kept = new HashSet<Guid>(reread.Desired.Select(task => task.TaskId));
        Assert.Equal(
            Edges(original.Draft.Tasks).Where(edge => kept.Contains(edge.Item1)).ToHashSet(),
            Edges(reread.Draft.Tasks));
    }

    /// <summary>The state a fresh import would leave each task in, as a board task.</summary>
    private static BoardTask AsBoardTask(BoardTaskDraft draft, IReadOnlyList<DesiredTask> desired)
    {
        var want = desired.Single(task => task.TaskId == draft.TaskId);
        var status = want.BlockedReason is not null ? TaskBoardStatus.Blocked : want.State switch
        {
            TodoState.Done => TaskBoardStatus.Done,
            TodoState.InProgress => TaskBoardStatus.InProgress,
            TodoState.NeedsSteve or TodoState.Deferred => TaskBoardStatus.Blocked,
            TodoState.Cancelled => TaskBoardStatus.Cancelled,
            _ => TaskBoardStatus.Open,
        };
        var claim = status == TaskBoardStatus.InProgress
            ? new TaskClaim(new TaskActor(want.Owner!.ToLowerInvariant(), TaskActorKind.Agent),
                new DateTimeOffset(want.ClaimedOn!.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero))
            : null;
        return new BoardTask(
            draft.TaskId, draft.Title, draft.Description, status, draft.Priority, draft.Position,
            draft.SubTaskOrdering, claim, 1, draft.PrerequisiteTaskIds,
            [.. draft.AcceptanceCriteria.Select(criterion =>
                new AcceptanceCriterion(criterion.CriterionId, criterion.Description, criterion.Position, false, null, null))],
            [.. draft.SubTasks.Select(child => AsBoardTask(child, desired))], at);
    }

    private static HashSet<(Guid, Guid?)> Edges(IReadOnlyList<BoardTaskDraft> tasks, Guid? parent = null)
    {
        var edges = new HashSet<(Guid, Guid?)>();
        foreach (var task in tasks)
        {
            edges.Add((task.TaskId, parent));
            edges.UnionWith(task.PrerequisiteTaskIds.Select(id => (task.TaskId, (Guid?)id)));
            edges.UnionWith(Edges(task.SubTasks, task.TaskId));
        }

        return edges;
    }

    private static BoardTask Leaf(
        string title, TaskBoardStatus status, TaskClaim? claim = null, string? description = null,
        IReadOnlyList<Guid>? prerequisites = null, IReadOnlyList<AcceptanceCriterion>? criteria = null)
    {
        return new BoardTask(
            Guid.NewGuid(), title, description ?? $"- [ ] {title}", status, TaskPriority.Normal, 0,
            TaskOrdering.Ordered, claim, 1, prerequisites ?? [], criteria ?? [], [], at);
    }

    private static BoardTask Root(string title, IReadOnlyList<BoardTask> children)
    {
        var positioned = children.Select((child, index) => child with { Position = index }).ToArray();
        return new BoardTask(
            Guid.NewGuid(), title, "epic", TaskBoardStatus.Open, TaskPriority.Normal, 0,
            TaskOrdering.Ordered, null, 1, [], [], positioned, at);
    }

    private static TaskBoardSnapshot Board(params BoardTask[] roots)
    {
        var positioned = roots.Select((root, index) => root with { Position = index }).ToArray();
        return new TaskBoardSnapshot(
            Guid.NewGuid(), "Dami Core suite", "request", "plan", claude, at, at,
            TaskBoardStatus.InProgress, TaskOrdering.Ordered, positioned);
    }

    private static string FindBoard()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            var candidate = Path.Combine(directory, "TODO.md");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
        }

        throw new FileNotFoundException("TODO.md was not found above the test directory.");
    }
}
