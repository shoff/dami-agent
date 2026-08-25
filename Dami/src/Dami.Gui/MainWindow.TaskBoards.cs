using Avalonia.Controls;
using Avalonia.Interactivity;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.TaskBoard;

namespace Dami.Gui;

/// <summary>Live collaborative task-board behavior over the localhost runtime.</summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan boardPollInterval = TimeSpan.FromSeconds(5);
    private readonly TaskBoardClient taskBoardClient = new(RuntimeClient.CreateHttpClient());

    private ListBox boardPicker = null!;
    private TreeView taskTree = null!;
    private TextBox featureRequest = null!;
    private TextBox boardActor = null!;
    private TextBox statusDetail = null!;
    private ComboBox plannerPicker = null!;
    private ComboBox privacyPicker = null!;
    private ComboBox actorKindPicker = null!;
    private Button planButton = null!;
    private Button boardRefresh = null!;

    private void InitializeTaskBoards()
    {
        this.boardPicker = Require<ListBox>(this, "BoardPicker");
        this.taskTree = Require<TreeView>(this, "TaskTree");
        this.featureRequest = Require<TextBox>(this, "FeatureRequest");
        this.boardActor = Require<TextBox>(this, "BoardActor");
        this.statusDetail = Require<TextBox>(this, "StatusDetail");
        this.plannerPicker = Require<ComboBox>(this, "PlannerPicker");
        this.privacyPicker = Require<ComboBox>(this, "PrivacyPicker");
        this.actorKindPicker = Require<ComboBox>(this, "ActorKindPicker");
        this.planButton = Require<Button>(this, "PlanButton");
        this.boardRefresh = Require<Button>(this, "BoardRefresh");
        this.boardPicker.SelectionChanged += this.OnBoardSelected;
        this.taskTree.AddHandler(Button.ClickEvent, this.OnTaskAction);
        this.planButton.Click += this.OnPlanFeature;
        this.boardRefresh.Click += this.OnBoardRefresh;
        _ = this.FollowBoardsAsync();
    }

    private async Task FollowBoardsAsync()
    {
        while (!this.lifetime.IsCancellationRequested)
        {
            await this.RefreshBoardsSafelyAsync().ConfigureAwait(true);
            try
            {
                await Task.Delay(boardPollInterval, this.lifetime.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RefreshBoardsSafelyAsync()
    {
        try
        {
            await this.RefreshBoardsAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            this.state.TaskBoards.Message = $"task boards unavailable: {exception.Message}";
        }
    }

    private async Task RefreshBoardsAsync()
    {
        var selectedId = (this.boardPicker.SelectedItem as TaskBoardSummary)?.BoardId;
        var boards = await this.taskBoardClient.ListAsync(100, this.lifetime.Token)
            .ConfigureAwait(true);
        Replace(this.state.TaskBoards.Boards, boards);
        this.boardPicker.SelectedItem = boards.FirstOrDefault(item => item.BoardId == selectedId)
            ?? boards.FirstOrDefault();
        if (this.boardPicker.SelectedItem is null)
        {
            this.state.TaskBoards.Message = "no plans yet";
        }
    }

    private void OnBoardSelected(object? sender, SelectionChangedEventArgs e)
    {
        _ = this.RefreshSelectedBoardSafelyAsync();
    }

    private async Task RefreshSelectedBoardSafelyAsync()
    {
        if (this.boardPicker.SelectedItem is not TaskBoardSummary summary)
        {
            return;
        }

        try
        {
            var board = await this.taskBoardClient.FindAsync(
                summary.BoardId, this.lifetime.Token).ConfigureAwait(true);
            var activity = await this.taskBoardClient.ActivityAsync(
                summary.BoardId, 100, this.lifetime.Token).ConfigureAwait(true);
            if (board is null)
            {
                return;
            }

            this.state.TaskBoards.Title = board.Title;
            this.state.TaskBoards.Detail = $"{board.Status} · {board.FeatureRequest} · {board.Plan}";
            Replace(this.state.TaskBoards.Tasks, board.Tasks.Select(TaskBoardTaskNode.From));
            Replace(this.state.TaskBoards.Activity, activity.Reverse());
            this.state.TaskBoards.Message = $"live · updated {board.UpdatedAt.ToLocalTime():T}";
        }
        catch (Exception exception)
        {
            this.state.TaskBoards.Message = $"board refresh failed: {exception.Message}";
        }
    }

    private void OnTaskAction(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button button || button.Tag is not string action)
        {
            return;
        }

        e.Handled = true;
        try
        {
            _ = button.DataContext switch
            {
                TaskBoardTaskNode task => this.MutateTaskAsync(task, action),
                TaskBoardCriterionNode criterion when action == "Criterion" =>
                    this.MutateCriterionAsync(criterion),
                _ => Task.CompletedTask,
            };
        }
        catch (Exception exception)
        {
            this.state.TaskBoards.Message = $"task change failed: {exception.Message}";
        }
    }

    private Task MutateTaskAsync(TaskBoardTaskNode task, string action)
    {
        var actor = this.ReadActor();
        return action switch
        {
            "Claim" => this.ApplyMutationAsync(() => this.taskBoardClient.ClaimAsync(
                task.TaskId, task.Version, actor, this.lifetime.Token)),
            "Complete" => this.ApplyMutationAsync(() => this.taskBoardClient.CompleteAsync(
                task.TaskId, task.Version, actor, this.lifetime.Token)),
            "Block" => this.ChangeStatusAsync(task, TaskBoardStatus.Blocked, actor),
            "Reopen" => this.ChangeStatusAsync(task, TaskBoardStatus.Open, actor),
            "Cancel" => this.ChangeStatusAsync(task, TaskBoardStatus.Cancelled, actor),
            _ => Task.CompletedTask,
        };
    }

    private Task MutateCriterionAsync(TaskBoardCriterionNode criterion)
    {
        var actor = this.ReadActor();
        return this.ApplyMutationAsync(() => this.taskBoardClient.SetCriterionAsync(
            criterion.CriterionId, criterion.ExpectedTaskVersion, !criterion.IsSatisfied,
            actor, this.lifetime.Token));
    }

    private Task ChangeStatusAsync(
        TaskBoardTaskNode task,
        TaskBoardStatus status,
        TaskActor actor)
    {
        var detail = this.statusDetail.Text?.Trim();
        if (string.IsNullOrEmpty(detail))
        {
            this.state.TaskBoards.Message = "a reason is required for status changes";
            return Task.CompletedTask;
        }

        return this.ApplyMutationAsync(() => this.taskBoardClient.SetStatusAsync(
            task.TaskId, task.Version, status, detail, actor, this.lifetime.Token));
    }

    private async Task ApplyMutationAsync(Func<Task<TaskBoardMutationOutcome>> mutation)
    {
        try
        {
            var outcome = await mutation().ConfigureAwait(true);
            this.state.TaskBoards.Message = outcome == TaskBoardMutationOutcome.Conflict
                ? "another actor changed this task; refreshing"
                : "task updated";
            await this.RefreshBoardsAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            this.state.TaskBoards.Message = $"task change failed: {exception.Message}";
        }
    }

    private void OnPlanFeature(object? sender, RoutedEventArgs e)
    {
        _ = this.PlanFeatureAsync();
    }

    private async Task PlanFeatureAsync()
    {
        var request = this.featureRequest.Text?.Trim();
        if (string.IsNullOrEmpty(request))
        {
            this.state.TaskBoards.Message = "a feature request is required";
            return;
        }

        try
        {
            var boardId = await this.taskBoardClient.PlanAsync(
                Guid.NewGuid(), request, this.ReadActor(),
                ReadEnum<FeaturePlannerKind>(this.plannerPicker),
                ReadEnum<PrivacyClass>(this.privacyPicker), ExecutionOrigin.UserTurn,
                this.lifetime.Token).ConfigureAwait(true);
            this.featureRequest.Text = string.Empty;
            await this.RefreshBoardsAsync().ConfigureAwait(true);
            this.boardPicker.SelectedItem = this.state.TaskBoards.Boards
                .FirstOrDefault(item => item.BoardId == boardId);
        }
        catch (Exception exception)
        {
            this.state.TaskBoards.Message = $"planning failed: {exception.Message}";
        }
    }

    private void OnBoardRefresh(object? sender, RoutedEventArgs e)
    {
        _ = this.RefreshBoardsSafelyAsync();
    }

    private TaskActor ReadActor()
    {
        return new TaskActor(
            this.boardActor.Text?.Trim() ?? string.Empty,
            ReadEnum<TaskActorKind>(this.actorKindPicker));
    }

    private static T ReadEnum<T>(ComboBox picker)
        where T : struct, Enum
    {
        var value = (picker.SelectedItem as ComboBoxItem)?.Content?.ToString();
        return Enum.Parse<T>(value ?? string.Empty);
    }

    private static void Replace<T>(
        System.Collections.ObjectModel.ObservableCollection<T> target,
        IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }
}
