using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Dami.Contracts.TaskBoard;

namespace Dami.Gui;

/// <summary>A native plan browser, flat task list and separate task inspector.</summary>
public sealed partial class TaskBoardWorkspace : UserControl
{
    private bool rendering;

    /// <summary>Constructs the workspace without starting network operations.</summary>
    public TaskBoardWorkspace()
    {
        AvaloniaXamlLoader.Load(this);
        this.DataContext = this.State;
        this.InitializeActions();
        this.Control<TextBox>("BoardActor").TextChanged += (_, _) => this.RenderOwnership();
        this.Control<ComboBox>("ActorKindPicker").SelectionChanged += (_, _) => this.RenderOwnership();
        this.Control<ListBox>("BoardPicker").SelectionChanged += (_, _) => this.OnBoardSelection();
        this.Control<TextBox>("PlanSearch").TextChanged += (_, _) => this.FilterBoardPicker();
        this.Control<TextBox>("TaskSearch").TextChanged += (_, _) => this.FilterTasks();
        this.Control<ComboBox>("TaskFilter").SelectionChanged += (_, _) => this.FilterTasks();
        this.Control<ListBox>("TaskList").SelectionChanged += (_, _) => this.SelectTask();
        this.Control<Button>("ClearTaskFilters").Click += (_, _) => this.ClearFilters();
        this.SizeChanged += (_, _) => this.SizeColumns();
    }

    /// <summary>The displayed board and current task selection.</summary>
    public TaskBoardPanelState State { get; } = new();

    /// <summary>Changes boards without displaying the old board's detail.</summary>
    public void SelectBoard(Guid? boardId)
    {
        if (this.State.BoardId != boardId)
        {
            this.Control<ReplyView>("PlanRequest").Text = string.Empty;
            this.Control<ReplyView>("PlanApproach").Text = string.Empty;
        }

        this.State.SelectBoard(boardId);
        this.RenderTasks();
    }

    /// <summary>Refreshes a snapshot while preserving a still-visible task selection.</summary>
    public void Present(TaskBoardSnapshot board)
    {
        this.rendering = true;
        this.State.Present(board);
        if (this.State.BoardId == board.BoardId)
        {
            this.Control<ReplyView>("PlanRequest").Text = board.FeatureRequest;
            this.Control<ReplyView>("PlanApproach").Text = board.Plan;
        }

        this.RenderTasks();
    }

    internal T Control<T>(string name) where T : Control => this.FindControl<T>(name)
        ?? throw new InvalidOperationException($"Task workspace control '{name}' is missing.");

    private void FilterTasks()
    {
        this.rendering = true;
        var choice = this.Control<ComboBox>("TaskFilter").SelectedItem as ComboBoxItem;
        var view = Enum.TryParse<BoardView>(choice?.Tag?.ToString(), out var parsed) ? parsed : BoardView.Active;
        this.State.Filter(view, this.Control<TextBox>("TaskSearch").Text ?? string.Empty);
        this.RenderTasks();
    }

    private void RenderTasks()
    {
        this.Control<ListBox>("TaskList").SelectedItem = this.State.Selected;
        this.Control<StackPanel>("TasksEmpty").IsVisible = this.State.Tasks.Count == 0;
        this.Control<StackPanel>("TaskWelcome").IsVisible = !this.State.HasSelection;
        this.Control<TextBlock>("BoardProgress").Text =
            $"{this.State.Tasks.Count} shown · {this.State.AllCount} total · {this.State.BlockedCount} blocked";
        this.RenderOwnership();
        this.RestoreInspectorDraft();
        this.RenderAdvice();
        this.Control<StackPanel>("TaskDetail").DataContext = this.State.Selected;
        this.Control<ReplyView>("TaskDescription").Text = this.State.Selected?.Description ?? string.Empty;
        this.rendering = false;
    }

    private void SelectTask()
    {
        if (this.rendering)
        {
            return;
        }

        this.State.Selected = this.Control<ListBox>("TaskList").SelectedItem as TaskBoardTaskNode;
        this.Control<StackPanel>("TaskWelcome").IsVisible = !this.State.HasSelection;
        this.RenderOwnership();
        this.RestoreInspectorDraft();
        this.RenderAdvice();
        this.Control<StackPanel>("TaskDetail").DataContext = this.State.Selected;
        this.Control<ReplyView>("TaskDescription").Text = this.State.Selected?.Description ?? string.Empty;
    }

    private void ClearFilters()
    {
        this.Control<TextBox>("TaskSearch").Text = string.Empty;
        this.Control<ComboBox>("TaskFilter").SelectedIndex = 6;
    }

    private void SizeColumns()
    {
        var columns = this.Control<Grid>("BoardColumns").ColumnDefinitions;
        columns[0].Width = new GridLength(this.Bounds.Width < 1000 ? 150 : 180);
        columns[4].Width = new GridLength(this.Bounds.Width < 1000 ? 320 : 380);
    }
}
