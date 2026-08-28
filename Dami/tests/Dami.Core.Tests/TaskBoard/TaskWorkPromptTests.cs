using Dami.Contracts.TaskBoard;
using Dami.Core.TaskBoard;
using Xunit;

namespace Dami.Core.Tests.TaskBoard;

public sealed class TaskWorkPromptTests
{
    private static BoardTask Task(
        string title,
        string description,
        TaskBoardStatus status = TaskBoardStatus.Open,
        params AcceptanceCriterion[] criteria)
    {
        return new BoardTask(
            Guid.NewGuid(), title, description, status, TaskPriority.Normal, 0,
            TaskOrdering.Ordered, null, 1, [], criteria, []);
    }

    private static AcceptanceCriterion Criterion(string description, bool satisfied)
    {
        return new AcceptanceCriterion(Guid.NewGuid(), description, 0, satisfied, null, null);
    }

    [Fact]
    public void Build_Should_Carry_The_Board_And_Task_So_The_Turn_Knows_What_It_Is_On()
    {
        var prompt = TaskWorkPrompt.Build(
            "Dami Core suite", Task("A6 PostgreSQL major version", "ADR-0016 proposed"));

        Assert.Contains("Dami Core suite", prompt, StringComparison.Ordinal);
        Assert.Contains("A6 PostgreSQL major version", prompt, StringComparison.Ordinal);
        Assert.Contains("ADR-0016 proposed", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_Should_List_Acceptance_Criteria_With_Their_Current_State()
    {
        // The criteria are what the task is measured against, so a proposal that ignores
        // them is not a proposal. Their satisfied state matters too: the model should not
        // re-argue a point already evidenced.
        var prompt = TaskWorkPrompt.Build("board", Task(
            "task", "scope", TaskBoardStatus.Open,
            Criterion("a restore has been rehearsed", true),
            Criterion("the destination is chosen", false)));

        Assert.Contains("a restore has been rehearsed", prompt, StringComparison.Ordinal);
        Assert.Contains("the destination is chosen", prompt, StringComparison.Ordinal);
        Assert.Contains("satisfied", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_Should_Ask_For_An_Artifact_Rather_Than_Recite_Prohibitions()
    {
        // Regression. The first version ended on what the model must not do — "you
        // cannot change the board", "say what is missing and stop" — and qwen3:8b took
        // it as licence to decline, answering that it lacked the authority to act. The
        // boundary lives in code and SQL; the prompt's job is to ask for the work.
        var prompt = TaskWorkPrompt.Build("board", Task("task", "scope"));

        Assert.DoesNotContain("You cannot", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("must not", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("and stop", prompt, StringComparison.Ordinal);
        Assert.Contains("take a position", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_Should_Tell_The_Model_To_Reason_Past_A_Missing_Fact()
    {
        // The other half of the same failure: told to stop at anything it did not have,
        // a small model stops immediately. It should state the assumption and continue.
        var prompt = TaskWorkPrompt.Build("board", Task("task", "scope"));

        Assert.Contains("assumption", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_Should_Say_So_When_A_Task_Has_No_Criteria()
    {
        // Most imported tasks have none. Silence there reads to the model as "no
        // constraints"; it should know the gate is simply unwritten.
        var prompt = TaskWorkPrompt.Build("board", Task("task", "scope"));

        Assert.Contains("no acceptance criteria", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_Should_Tolerate_An_Empty_Description()
    {
        var prompt = TaskWorkPrompt.Build("board", Task("bare title", string.Empty));

        Assert.Contains("bare title", prompt, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(prompt));
    }

    [Fact]
    public void Build_Should_Reject_A_Missing_Task()
    {
        Assert.Throws<ArgumentNullException>(() => TaskWorkPrompt.Build("board", null!));
    }
}
