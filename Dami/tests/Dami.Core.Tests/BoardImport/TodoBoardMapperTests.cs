using Dami.Contracts.TaskBoard;
using Dami.Core.BoardImport;
using Xunit;

namespace Dami.Core.Tests.BoardImport;

/// <summary>Covers the translation from the parsed file onto the board's contracts.</summary>
public sealed class TodoBoardMapperTests
{
    private static readonly TaskActor actor = new("claude", TaskActorKind.Agent);
    private static readonly DateTimeOffset importedAt = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Map_Should_Make_Each_Epic_Section_A_Root_Task()
    {
        var plan = Map("""
            ## A · Host & infrastructure

            - [ ] A1 Provision the host

            ## B · Data foundation

            - [ ] B1 Schema
            """);

        Assert.Equal(2, plan.Draft.Tasks.Count);
        Assert.Equal("A · Host & infrastructure", plan.Draft.Tasks[0].Title);
        Assert.Equal("A1", Assert.Single(plan.Draft.Tasks[0].SubTasks).Title[..2]);
    }

    [Fact]
    public void Map_Should_Nest_Sub_Tasks_To_Any_Depth_As_The_Same_Type()
    {
        var plan = Map("""
            ## G · Runtime

            - [x] G5 Parent
              - [x] G5a Child
                - [ ] G5a1 Grandchild
            """);

        var g5 = plan.Draft.Tasks[0].SubTasks[0];
        var g5a = Assert.Single(g5.SubTasks);
        var g5a1 = Assert.Single(g5a.SubTasks);
        Assert.StartsWith("G5a1", g5a1.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_Should_Give_The_Same_Task_The_Same_Id_Every_Run()
    {
        const string markdown = """
            ## G · Runtime

            - [ ] G5a1 Deep task
            """;

        Assert.Equal(
            Map(markdown).Draft.Tasks[0].SubTasks[0].TaskId,
            Map(markdown).Draft.Tasks[0].SubTasks[0].TaskId);
    }

    [Fact]
    public void Map_Should_Give_Different_Tasks_Different_Ids()
    {
        var plan = Map("""
            ## G · Runtime

            - [ ] G5 One
            - [ ] G6 Two
            """);

        Assert.NotEqual(plan.Draft.Tasks[0].SubTasks[0].TaskId, plan.Draft.Tasks[0].SubTasks[1].TaskId);
    }

    [Fact]
    public void Map_Should_Keep_The_Original_Line_In_The_Description()
    {
        var plan = Map("""
            ## G · Runtime

            - [x] G2 Context assembly — hard token budget
            """);

        Assert.Contains(
            "- [x] G2 Context assembly — hard token budget",
            plan.Draft.Tasks[0].SubTasks[0].Description,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Map_Should_Order_Siblings_By_File_Position()
    {
        var plan = Map("""
            ## A · Host

            - [ ] A1 First
            - [ ] A2 Second
            """);

        var subTasks = plan.Draft.Tasks[0].SubTasks;
        Assert.Equal([0, 1], subTasks.Select(task => task.Position));
        Assert.Equal(TaskOrdering.Ordered, plan.Draft.Tasks[0].SubTaskOrdering);
    }

    [Fact]
    public void Map_Should_Turn_Acceptance_References_Into_Criteria()
    {
        var plan = Map("""
            ## G · Runtime

            - [x] G4 Sessions — acceptance item 1
            """);

        var criterion = Assert.Single(plan.Draft.Tasks[0].SubTasks[0].AcceptanceCriteria);
        Assert.Equal("acceptance item 1", criterion.Description);
    }

    [Fact]
    public void Map_Should_Turn_A_Resolved_Dependency_Into_A_Prerequisite_Edge()
    {
        var plan = Map("""
            ## H · Proactive

            - [ ] H9 Domain collectors — needs K1 first

            ## K · Domains

            - [ ] K1 Domain inventory
            """);

        var h9 = plan.Draft.Tasks[0].SubTasks[0];
        var k1 = plan.Draft.Tasks[1].SubTasks[0];
        Assert.Equal([k1.TaskId], h9.PrerequisiteTaskIds);
    }

    [Theory]
    [InlineData("- [ ] A1 Open", TodoState.Open)]
    [InlineData("- [x] A1 Done", TodoState.Done)]
    [InlineData("- [STEVE] A1 Waiting", TodoState.NeedsSteve)]
    public void Map_Should_Carry_The_Desired_State_For_Each_Task(string line, TodoState expected)
    {
        var plan = Map($"## A · Host\n\n{line}\n");

        var desired = plan.Desired.Single(task => task.TodoId == "A1");
        Assert.Equal(expected, desired.State);
    }

    [Fact]
    public void Map_Should_Carry_The_Owner_And_Claim_Date_Of_A_Claimed_Task()
    {
        var plan = Map("## O · Board\n\n- [~ Codex 2026-08-24] O1a Contracts\n");

        var desired = plan.Desired.Single(task => task.TodoId == "O1a");
        Assert.Equal(TodoState.InProgress, desired.State);
        Assert.Equal("Codex", desired.Owner);
        Assert.Equal(new DateOnly(2026, 8, 24), desired.ClaimedOn);
    }

    [Fact]
    public void Map_Should_Complete_An_Epic_Whose_Every_Child_Is_Done()
    {
        // An entailment, not a guess: a parent whose every child is done is done, and the
        // store's completion rule requires exactly that before it will accept one.
        var plan = Map("""
            ## E · Transport

            - [x] E1 Framing
            - [x] E2 Loopback
            """);

        Assert.Equal(TodoState.Done, plan.Desired.Single(task => task.TodoId == "E").State);
    }

    [Fact]
    public void Map_Should_Leave_An_Epic_With_Unfinished_Children_Open()
    {
        var plan = Map("""
            ## E · Transport

            - [x] E1 Framing
            - [ ] E2 Loopback
            """);

        Assert.Equal(TodoState.Open, plan.Desired.Single(task => task.TodoId == "E").State);
    }

    [Fact]
    public void Map_Should_Derive_An_Id_For_An_Entry_Whose_Own_Id_Is_Missing()
    {
        var plan = Map("""
            ## G · Runtime

            - [x] G9 Frontier turns
            - [STEVE] ~~G9~~ posture question
            """);

        // The struck-through entry has no id of its own; it must still get a stable identity
        // and must not collide with the live G9.
        var tasks = plan.Draft.Tasks[0].SubTasks;
        Assert.Equal(2, tasks.Count);
        Assert.NotEqual(tasks[0].TaskId, tasks[1].TaskId);
    }

    [Fact]
    public void Map_Should_Reject_A_Document_With_No_Sections()
    {
        Assert.Throws<ArgumentException>(() => Map("no sections here"));
    }

    private static TodoImportPlan Map(string markdown)
    {
        return TodoBoardMapper.Map(
            TodoBoardParser.Parse(markdown),
            new TodoImportSource("abc1234", "the end state", "imported from TODO.md"),
            actor,
            importedAt);
    }
}
