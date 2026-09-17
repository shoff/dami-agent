using Avalonia.Controls;
using Dami.Contracts.TaskBoard;

namespace Dami.Gui;

/// <summary>Coordinates explicit workspace actions and bounded runtime reads.</summary>
public sealed partial class TaskBoardWorkspaceController
{
    private readonly TaskBoardClient client;
    private readonly TaskBoardWorkspace view;
    private readonly CancellationToken cancellationToken;
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private bool changing;
    private long snapshotRequest;

    /// <summary>Creates a coordinator over an injected runtime client and native view.</summary>
    public TaskBoardWorkspaceController(TaskBoardClient client, TaskBoardWorkspace view, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(view);
        this.client = client;
        this.view = view;
        this.cancellationToken = cancellationToken;
    }

    /// <summary>Reads current plans and the selected board without overlapping list polls.</summary>
    public async Task RefreshAsync()
    {
        await this.refreshGate.WaitAsync(this.cancellationToken).ConfigureAwait(true);
        try
        {
            this.view.PresentBoards(await this.client.ListAsync(100, this.cancellationToken).ConfigureAwait(true));
            await this.RefreshSelectedAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            this.view.State.Message = $"Could not refresh plans: {exception.Message}";
        }
        finally
        {
            this.refreshGate.Release();
        }
    }

    /// <summary>Ignores a late response after selection or a newer request has changed.</summary>
    public async Task RefreshSelectedAsync()
    {
        var request = ++this.snapshotRequest;
        if (this.view.State.BoardId is not { } boardId)
        {
            return;
        }

        try
        {
            var board = await this.client.FindAsync(boardId, this.cancellationToken).ConfigureAwait(true);
            if (request != this.snapshotRequest || boardId != this.view.State.BoardId || board is null)
            {
                return;
            }

            this.view.Present(board);
            if (this.view.State.Message == "loading task boards…")
            {
                this.view.State.Message = "Connected to the shared task board. Task actions are saved immediately.";
            }
            await this.RefreshActivityAsync(boardId, request).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            if (request == this.snapshotRequest)
            {
                this.view.State.Message = $"Could not load this plan: {exception.Message}";
            }
        }
    }

    private async Task RefreshActivityAsync(Guid boardId, long request)
    {
        var activity = await this.client.ActivityAsync(boardId, 500, this.cancellationToken).ConfigureAwait(true);
        if (request == this.snapshotRequest && boardId == this.view.State.BoardId)
        {
            Reconcile.Sync(this.view.State.Activity, activity.Reverse().ToArray());
            this.view.Control<TextBlock>("ActivityNote").Text = activity.Count == 500
                ? "Showing the first 500 recorded events, latest of those first."
                : $"{activity.Count} recorded events · latest first";
        }
    }

    /// <summary>Runs one action and retains all draft inputs when a request fails.</summary>
    public async Task ExecuteAsync(string action, object? target)
    {
        if (this.changing)
        {
            return;
        }

        this.changing = true;
        this.view.SetBusy(true);
        try
        {
            await this.DispatchAsync(action, target).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            if (action == "Work" && target is TaskBoardTaskNode task)
            {
                this.view.ShowAdvice(task.TaskId, string.Empty, $"Advice failed: {exception.Message}");
            }

            this.view.State.Message = $"{action} failed: {exception.Message}. Your inputs are still here.";
        }
        finally
        {
            this.changing = false;
            this.view.SetBusy(false);
        }
    }

    private async Task AddTaskAsync(TaskBoardEntry entry)
    {
        var result = await this.client.AddTaskAsync(entry.BoardId, entry.ParentTaskId, entry.TaskId,
            entry.Title, entry.Description, entry.Criteria, this.ReadActor(), this.cancellationToken).ConfigureAwait(true);
        if (result == TaskBoardMutationOutcome.Conflict)
        {
            this.view.State.Message = "Task was not added. The parent may have changed or this draft may already exist. Refresh and check before retrying.";
            return;
        }

        this.view.TaskSaved(entry);
        await this.SelectSavedBoardAsync(entry.BoardId).ConfigureAwait(true);
        this.view.FocusTask(entry.TaskId);
        this.view.State.Message = "Task added.";
    }

    private async Task SelectSavedBoardAsync(Guid boardId)
    {
        await this.RefreshAsync().ConfigureAwait(true);
        this.view.SelectBoard(boardId);
        this.view.PresentBoards(this.view.State.Boards.ToArray());
        await this.RefreshSelectedAsync().ConfigureAwait(true);
    }

    private TaskActor ReadActor() => new(
        this.view.Control<TextBox>("BoardActor").Text?.Trim() ?? string.Empty,
        this.ReadChoice<TaskActorKind>("ActorKindPicker"));

    private T ReadChoice<T>(string name) where T : struct, Enum => Enum.Parse<T>(
        (this.view.Control<ComboBox>(name).SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty);
}
