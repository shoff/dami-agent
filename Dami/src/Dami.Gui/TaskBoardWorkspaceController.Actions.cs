using Avalonia.Controls;
using Avalonia.Input.Platform;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.TaskBoard;

namespace Dami.Gui;

public sealed partial class TaskBoardWorkspaceController
{
    private Task DispatchAsync(string action, object? target) => action switch
    {
        "Refresh" => this.RefreshAsync(),
        "CopyCode" when target is string code => this.CopyCodeAsync(code),
        "Plan" => this.PlanAsync(),
        "AddTask" when target is TaskBoardEntry entry => this.AddTaskAsync(entry),
        "Criterion" when target is TaskBoardCriterionNode criterion => this.MutateAsync(
            () => this.client.SetCriterionAsync(criterion.CriterionId, criterion.ExpectedTaskVersion,
                !criterion.IsSatisfied, this.ReadActor(), this.cancellationToken), "Requirement updated."),
        "Work" when target is TaskBoardTaskNode task => this.AdviseAsync(task),
        _ when target is TaskBoardTaskNode task => this.ChangeTaskAsync(task, action),
        _ => Task.CompletedTask,
    };

    private async Task CopyCodeAsync(string code)
    {
        if (TopLevel.GetTopLevel(this.view)?.Clipboard is not { } clipboard)
        {
            this.view.State.Message = "Clipboard unavailable.";
            return;
        }

        await clipboard.SetTextAsync(code).ConfigureAwait(true);
        this.view.State.Message = "Code copied.";
    }

    private async Task ChangeTaskAsync(TaskBoardTaskNode task, string action)
    {
        var actor = this.ReadActor();
        switch (action)
        {
            case "Claim":
                await this.MutateAsync(() => this.client.ClaimAsync(task.TaskId, task.Version, actor, this.cancellationToken), "Task started.").ConfigureAwait(true);
                break;
            case "Complete":
                await this.MutateAsync(() => this.client.CompleteAsync(task.TaskId, task.Version, actor, this.cancellationToken), "Task completed.").ConfigureAwait(true);
                break;
            case "AddCriterion":
                await this.AddCriterionAsync(task, actor).ConfigureAwait(true);
                break;
            case "Block":
            case "Reopen":
            case "Cancel":
                await this.ChangeStatusAsync(task, actor, action).ConfigureAwait(true);
                break;
        }
    }

    private async Task<bool> MutateAsync(Func<Task<TaskBoardMutationOutcome>> pending, string success)
    {
        var outcome = await pending().ConfigureAwait(true);
        await this.RefreshSelectedAsync().ConfigureAwait(true);
        this.view.State.Message = outcome == TaskBoardMutationOutcome.Updated ? success
            : "Change not applied. The task was refreshed: check its owner, requirements, dependencies, and current status before trying again.";
        return outcome == TaskBoardMutationOutcome.Updated;
    }

    private async Task AddCriterionAsync(TaskBoardTaskNode task, TaskActor actor)
    {
        var field = this.view.Control<TextBox>("NewCriterion");
        var text = field.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            this.view.State.Message = "Describe what must be true before this task is done.";
            return;
        }

        var saved = await this.MutateAsync(() => this.client.AddCriterionAsync(task.TaskId, task.Version,
            text, actor, this.cancellationToken), "Requirement added.").ConfigureAwait(true);
        if (saved && this.view.State.Selected?.TaskId == task.TaskId)
        {
            field.Text = string.Empty;
        }
    }

    private async Task ChangeStatusAsync(TaskBoardTaskNode task, TaskActor actor, string action)
    {
        var reason = this.view.Control<TextBox>("StatusDetail").Text?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            this.view.State.Message = "Enter a reason under Change status first.";
            return;
        }

        var status = action switch
        {
            "Block" => TaskBoardStatus.Blocked,
            "Reopen" => TaskBoardStatus.Open,
            _ => TaskBoardStatus.Cancelled,
        };
        await this.MutateAsync(() => this.client.SetStatusAsync(task.TaskId, task.Version,
            status, reason, actor, this.cancellationToken), $"Task {status.ToString().ToLowerInvariant()}.").ConfigureAwait(true);
    }

    private async Task PlanAsync()
    {
        var request = this.view.Control<TextBox>("FeatureRequest").Text?.Trim() ?? string.Empty;
        if (request.Length == 0)
        {
            this.view.State.Message = "Describe the outcome you want to plan.";
            return;
        }

        this.view.State.Message = "Creating your plan… This may take a moment.";
        var boardId = await this.client.PlanAsync(Guid.NewGuid(), request, this.ReadActor(),
            this.ReadChoice<FeaturePlannerKind>("PlannerPicker"), this.ReadChoice<PrivacyClass>("PrivacyPicker"),
            ExecutionOrigin.UserTurn, this.cancellationToken).ConfigureAwait(true);
        this.view.Control<TextBox>("FeatureRequest").Text = string.Empty;
        this.view.Control<Border>("PlanForm").IsVisible = false;
        await this.SelectSavedBoardAsync(boardId).ConfigureAwait(true);
        this.view.State.Message = "Plan created. Review its tasks and requirements before starting.";
    }

    private async Task AdviseAsync(TaskBoardTaskNode task)
    {
        if (this.view.State.BoardId is not { } boardId)
        {
            return;
        }

        this.view.ShowAdvice(task.TaskId, string.Empty, "Thinking about this task…");
        var reply = await this.client.WorkAsync(boardId, task.TaskId, this.ReadActor(),
            this.ReadChoice<FeaturePlannerKind>("AdvicePlanner"), this.cancellationToken).ConfigureAwait(true);
        this.view.ShowAdvice(task.TaskId, reply.Ran ? reply.Answer : reply.Reason ?? "No advice returned.",
            reply.Ran ? "Advice ready. Task status is unchanged." : "The advisory run did not start.");
        this.view.State.Message = reply.Ran ? "Advice is ready in the task's advice section." : $"Advice unavailable: {reply.Reason}";
    }
}
