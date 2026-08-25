using Dami.Contracts.TaskBoard;
using Dami.Core.BoardImport;
using Xunit;

namespace Dami.Core.Tests.BoardImport;

/// <summary>
/// The rerun rules. A second import must advance what the file says is further along and
/// must never pull the board back to what a stale file believes.
/// </summary>
public sealed class ImportStepTests
{
    private static readonly TaskActor importer = new("claude", TaskActorKind.Agent);
    private static readonly TaskActor someoneElse = new("codex", TaskActorKind.Agent);

    [Fact]
    public void Next_Should_Claim_An_Open_Task_The_File_Says_Is_Done()
    {
        Assert.Equal(
            ImportStepKind.Claim,
            Next(TodoState.Done, TaskBoardStatus.Open).Kind);
    }

    [Fact]
    public void Next_Should_Satisfy_Criteria_Before_Completing()
    {
        var step = Next(TodoState.Done, TaskBoardStatus.InProgress, claimedBy: importer, unsatisfied: true);

        Assert.Equal(ImportStepKind.SatisfyCriteria, step.Kind);
    }

    [Fact]
    public void Next_Should_Complete_Once_Its_Criteria_Are_Satisfied()
    {
        var step = Next(TodoState.Done, TaskBoardStatus.InProgress, claimedBy: importer);

        Assert.Equal(ImportStepKind.Complete, step.Kind);
    }

    [Fact]
    public void Next_Should_Do_Nothing_When_The_Board_Already_Agrees()
    {
        Assert.Equal(ImportStepKind.None, Next(TodoState.Done, TaskBoardStatus.Done).Kind);
        Assert.Equal(ImportStepKind.None, Next(TodoState.Open, TaskBoardStatus.Open).Kind);
    }

    [Fact]
    public void Next_Should_Not_Reopen_A_Task_The_Board_Has_Finished()
    {
        // The whole point of the rerun rule: someone finished G5a1 and has not yet ticked
        // the file. Importing the file's "open" over that would erase their work.
        var step = Next(TodoState.Open, TaskBoardStatus.Done);

        Assert.Equal(ImportStepKind.None, step.Kind);
    }

    [Fact]
    public void Next_Should_Report_Rather_Than_Regress_A_Finished_Task_The_File_Calls_In_Progress()
    {
        var step = Next(TodoState.InProgress, TaskBoardStatus.Done);

        Assert.Equal(ImportStepKind.Conflict, step.Kind);
        Assert.Contains("Done", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Next_Should_Not_Take_A_Task_Another_Actor_Has_Claimed()
    {
        var step = Next(TodoState.Done, TaskBoardStatus.InProgress, claimedBy: someoneElse);

        Assert.Equal(ImportStepKind.Conflict, step.Kind);
        Assert.Contains("codex", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Next_Should_Block_A_Task_Waiting_On_Steve()
    {
        var step = Next(TodoState.NeedsSteve, TaskBoardStatus.Open);

        Assert.Equal(ImportStepKind.Block, step.Kind);
        Assert.Contains("Steve", step.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Next_Should_Block_A_Deferred_Task_And_Say_It_Was_Deferred_Not_Cancelled()
    {
        var step = Next(TodoState.Deferred, TaskBoardStatus.Open, detail: "correct as-is");

        Assert.Equal(ImportStepKind.Block, step.Kind);
        Assert.Contains("Deferred", step.Detail, StringComparison.Ordinal);
        Assert.Contains("correct as-is", step.Detail, StringComparison.Ordinal);

        // The board has a Cancelled status and this must never reach it: the file said the
        // work was deliberately not built, which is not the same as abandoned.
        Assert.Contains("not cancelled", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Next_Should_Block_An_Open_Task_Carrying_A_Blocked_Annotation()
    {
        var step = Next(TodoState.Open, TaskBoardStatus.Open, blockedReason: "Mac access");

        Assert.Equal(ImportStepKind.Block, step.Kind);
        Assert.Contains("Mac access", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Next_Should_Leave_An_Already_Blocked_Task_Alone()
    {
        var step = Next(TodoState.NeedsSteve, TaskBoardStatus.Blocked);

        Assert.Equal(ImportStepKind.None, step.Kind);
    }

    [Fact]
    public void Next_Should_Claim_In_Progress_Work_As_The_Owner_Named_In_The_File()
    {
        var step = Next(TodoState.InProgress, TaskBoardStatus.Open, owner: "Codex");

        Assert.Equal(ImportStepKind.Claim, step.Kind);
        Assert.Equal("codex", step.Actor.ActorId);
    }

    [Fact]
    public void Next_Should_Claim_Unowned_Work_As_The_Importer()
    {
        var step = Next(TodoState.Done, TaskBoardStatus.Open);

        Assert.Equal("claude", step.Actor.ActorId);
    }

    private static ImportStep Next(
        TodoState state,
        TaskBoardStatus status,
        TaskActor? claimedBy = null,
        bool unsatisfied = false,
        string? owner = null,
        string? detail = null,
        string? blockedReason = null)
    {
        var taskId = Guid.NewGuid();
        var criterion = new AcceptanceCriterion(
            Guid.NewGuid(), "acceptance item 1", 0, !unsatisfied, null, null);

        var desired = new DesiredTask(
            taskId, "G5a1", state, owner, null, blockedReason, detail, [criterion.CriterionId], 1);
        var actual = new BoardTask(
            taskId,
            "G5a1 A task",
            "description",
            status,
            TaskPriority.Normal,
            0,
            TaskOrdering.Ordered,
            claimedBy is null ? null : new TaskClaim(claimedBy, DateTimeOffset.UnixEpoch),
            1,
            [],
            [criterion],
            []);

        return ImportStep.Next(desired, actual, importer);
    }
}
