using Avalonia.Controls;

namespace Dami.Gui;

/// <summary>Hosts the native task workspace and its bounded live refresh.</summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan boardPollInterval = TimeSpan.FromSeconds(5);
    private readonly TaskBoardClient taskBoardClient = new(RuntimeClient.CreateHttpClient());
    private TaskBoardWorkspaceController boardController = null!;

    private void InitializeTaskBoards()
    {
        var workspace = Require<TaskBoardWorkspace>(this, "BoardWorkspace");
        this.boardController = new TaskBoardWorkspaceController(this.taskBoardClient, workspace, this.lifetime.Token);
        workspace.ActionRequested += (action, target) => _ = this.boardController.ExecuteAsync(action, target);
        workspace.BoardSelected += () => _ = this.boardController.RefreshSelectedAsync();
        _ = this.FollowBoardsAsync();
        this.InitialiseWorkers();
        this.InitialiseAsk();
        Require<ItemsControl>(this, "AttentionList").AddHandler(Button.ClickEvent, this.OnAttentionAction);
    }

    private async Task FollowBoardsAsync()
    {
        while (!this.lifetime.IsCancellationRequested)
        {
            await this.boardController.RefreshAsync().ConfigureAwait(true);
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

    /// <summary>Reconciles shared observable feeds without clearing unchanged rows.</summary>
    private static void Replace<T>(
        System.Collections.ObjectModel.ObservableCollection<T> target,
        IEnumerable<T> values)
    {
        Reconcile.Sync(target, values as IReadOnlyList<T> ?? values.ToList());
    }
}
