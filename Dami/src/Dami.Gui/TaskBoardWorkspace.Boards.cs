using System.Collections.ObjectModel;
using Avalonia.Controls;
using Dami.Contracts.TaskBoard;

namespace Dami.Gui;

public sealed partial class TaskBoardWorkspace
{
    private bool renderingBoards;
    private readonly ObservableCollection<TaskBoardSummary> visibleBoards = [];

    /// <summary>A user-selected plan needs a fresh snapshot.</summary>
    public event Action? BoardSelected;

    /// <summary>Reconciles the board picker without triggering a polling feedback loop.</summary>
    public void PresentBoards(IReadOnlyList<TaskBoardSummary> boards)
    {
        ArgumentNullException.ThrowIfNull(boards);
        Reconcile.Sync(this.State.Boards, boards);
        var selected = boards.FirstOrDefault(board => board.BoardId == this.State.BoardId) ?? boards.FirstOrDefault();
        this.SelectBoard(selected?.BoardId);
        this.FilterBoardPicker();
        this.SetBusy(this.busy);
    }

    private void FilterBoardPicker()
    {
        this.renderingBoards = true;
        var query = this.Control<TextBox>("PlanSearch").Text?.Trim() ?? string.Empty;
        Reconcile.Sync(this.visibleBoards, this.State.Boards.Where(board =>
            board.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray());
        var picker = this.Control<ListBox>("BoardPicker");
        picker.ItemsSource = this.visibleBoards;
        picker.SelectedItem = this.visibleBoards.FirstOrDefault(board => board.BoardId == this.State.BoardId);
        this.Control<TextBlock>("PlansEmpty").IsVisible = this.visibleBoards.Count == 0;
        this.Control<TextBlock>("PlansEmpty").Text = this.State.Boards.Count == 0
            ? "No plans yet. Create a plan to get started." : "No plans match your search.";
        this.renderingBoards = false;
    }

    private void OnBoardSelection()
    {
        if (this.renderingBoards)
        {
            return;
        }

        this.SelectBoard((this.Control<ListBox>("BoardPicker").SelectedItem as TaskBoardSummary)?.BoardId);
        this.BoardSelected?.Invoke();
    }
}
