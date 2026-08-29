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
    private IReadOnlyList<TaskBoardTaskNode> roots = [];
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
        this.taskTree.SelectionChanged += this.OnTaskSelected;
        Require<Border>(this, "TaskActions").AddHandler(Button.ClickEvent, this.OnTaskAction);
        this.planButton.Click += this.OnPlanFeature;
        this.boardRefresh.Click += this.OnBoardRefresh;
        Require<StackPanel>(this, "BoardViews").AddHandler(Button.ClickEvent, this.OnBoardView);
        _ = this.FollowBoardsAsync();
        this.InitialiseWorkers();
        Require<ItemsControl>(this, "AttentionList")
            .AddHandler(Button.ClickEvent, this.OnAttentionAction);
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
            this.roots = board.Tasks.Select(TaskBoardTaskNode.From).ToList();
            this.ApplyBoardView();
            Replace(this.state.TaskBoards.Activity, activity.Reverse());
            this.state.TaskBoards.Message = $"live · updated {board.UpdatedAt.ToLocalTime():T}";
        }
        catch (Exception exception)
        {
            this.state.TaskBoards.Message = $"board refresh failed: {exception.Message}";
        }
    }

    /// <summary>
    /// Re-lists the tree for the selected view and refreshes the counts. Held separately
    /// from the fetch so pressing a filter costs nothing but a reconcile.
    /// </summary>
    private void ApplyBoardView()
    {
        var panel = this.state.TaskBoards;
        panel.NeedsYouCount = BoardFilter.Count(this.roots, BoardView.NeedsYou);
        panel.OpenCount = BoardFilter.Count(this.roots, BoardView.Open);
        panel.BlockedCount = BoardFilter.Count(this.roots, BoardView.Blocked);
        panel.AllCount = BoardFilter.Count(this.roots, BoardView.All);
        Replace(panel.Tasks, BoardFilter.Apply(this.roots, panel.View));
    }

    private void OnBoardView(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button button && button.Tag is string name
            && Enum.TryParse<BoardView>(name, out var view))
        {
            this.state.TaskBoards.View = view;
            this.ApplyBoardView();
        }
    }

    private void OnTaskSelected(object? sender, SelectionChangedEventArgs e)
    {
        this.state.TaskBoards.Selected = this.taskTree.SelectedItem as TaskBoardTaskNode;
    }

    private void OnTaskAction(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button button || button.Tag is not string action)
        {
            return;
        }

        e.Handled = true;
        Diagnostics.Write($"task action: tag={action} dataContext={button.DataContext?.GetType().Name ?? "null"}");
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
            "Work" => this.WorkOnTaskAsync(task, actor),
            _ => Task.CompletedTask,
        };
    }

    /// <summary>
    /// Asks the runtime to work this task now. The answer lands in the conversation
    /// because that is where long prose is already readable and scrollable, and the run
    /// itself streams into the execution graph on its own — the board only records that
    /// it happened.
    /// </summary>
    private async Task WorkOnTaskAsync(TaskBoardTaskNode task, TaskActor actor)
    {
        if (this.boardPicker.SelectedItem is not TaskBoardSummary board)
        {
            this.state.TaskBoards.Message = "select a board first";
            return;
        }

        var reply = this.OpenWorkExchange(task);

        try
        {
            // The same "Plan with" picker the planner uses. Steve's first real run went
            // to the local 8B by default and was useless; the choice has to be his.
            var planner = ReadEnum<FeaturePlannerKind>(this.plannerPicker);
            var outcome = await this.taskBoardClient
                .WorkAsync(board.BoardId, task.TaskId, actor, planner, this.lifetime.Token)
                .ConfigureAwait(true);
            Report(reply, outcome);
            await this.SpeakAsync(reply).ConfigureAwait(true);
            this.state.TaskBoards.Message = outcome.Ran
                ? "advisory run finished" : "advisory run refused";
            await this.RefreshBoardsAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            reply.Body = $"the run failed: {exception.Message}";
            reply.Meta = "advisory run failed";
            this.state.TaskBoards.Message = $"work failed: {exception.Message}";
        }

        ScrollLater(this.chatScroll);
    }

    /// <summary>Puts the request and a placeholder answer into the conversation.</summary>
    private Message OpenWorkExchange(TaskBoardTaskNode task)
    {
        this.state.TaskBoards.Message = $"working \"{task.Title}\"…";
        this.state.Messages.Add(new Message("you", $"work on this task: {task.Title}"));
        var reply = new Message("dami", "…");
        this.state.Messages.Add(reply);
        ScrollLater(this.chatScroll);
        return reply;
    }

    /// <summary>Shows what the run said, and that the task itself did not move.</summary>
    private static void Report(Message reply, TaskWorkReply outcome)
    {
        reply.Body = outcome.Ran ? outcome.Answer : $"the run did not start: {outcome.Reason}";
        reply.Meta = outcome.Ran
            ? $"advisory run · trace {outcome.TraceId:N} · the task is unchanged"
            : "advisory run refused";
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

    /// <remarks>
    /// Reconciles rather than clears. The board re-polls every five seconds; clearing
    /// raised a Reset, which collapsed every expanded task and threw away the scroll
    /// position mid-read. See <see cref="Reconcile"/>.
    /// </remarks>
    private static void Replace<T>(
        System.Collections.ObjectModel.ObservableCollection<T> target,
        IEnumerable<T> values)
    {
        Reconcile.Sync(target, values as IReadOnlyList<T> ?? values.ToList());
    }
}
