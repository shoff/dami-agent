using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Dami.Gui;

public sealed partial class TaskBoardWorkspace
{
    private TaskBoardEntry? entry;
    private bool busy;

    /// <summary>An explicit user action for the runtime controller to execute.</summary>
    public event Action<string, object?>? ActionRequested;

    /// <summary>Prevents another write while the current user action is pending.</summary>
    public void SetBusy(bool value)
    {
        this.busy = value;
        this.Control<Border>("PlanForm").IsEnabled = !value;
        this.Control<Border>("TaskForm").IsEnabled = !value;
        this.Control<StackPanel>("TaskDetail").IsEnabled = !value;
        this.Control<Button>("AddTask").IsEnabled = !value && this.State.BoardId.HasValue;
        this.Control<Button>("NewPlan").IsEnabled = !value;
    }

    /// <summary>Clears a successfully saved draft only after the server acknowledges it.</summary>
    public void TaskSaved(TaskBoardEntry saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if (this.entry?.TaskId != saved.TaskId)
        {
            return;
        }

        this.entry = null;
        this.Control<TextBox>("NewTaskTitle").Text = string.Empty;
        this.Control<TextBox>("NewTaskDescription").Text = string.Empty;
        this.Control<TextBox>("NewTaskCriteria").Text = string.Empty;
        this.Control<Border>("TaskForm").IsVisible = false;
    }

    private void InitializeActions()
    {
        this.Control<Button>("NewPlan").Click += (_, _) => this.ToggleForm("PlanForm", true);
        this.Control<Button>("ClosePlan").Click += (_, _) => this.ToggleForm("PlanForm", false);
        this.Control<Button>("CloseTask").Click += (_, _) => this.ToggleForm("TaskForm", false);
        this.Control<Button>("AddTask").Click += (_, _) => this.OpenTaskForm(null);
        this.Control<Button>("SaveTask").Click += (_, _) => this.SubmitTask();
        this.Control<Button>("PlanButton").Click += (_, _) => this.Request("Plan", null);
        this.Control<Button>("BoardRefresh").Click += (_, _) => this.Request("Refresh", null);
        this.AddHandler(Button.ClickEvent, this.OnContentAction);
    }

    internal void OnContentAction(object? sender, RoutedEventArgs args)
    {
        if (this.busy || args.Source is not Button button || button.Tag is not string action)
        {
            return;
        }

        args.Handled = true;
        switch (action)
        {
            case "copy-code": this.Request("CopyCode", button.CommandParameter); break;
            case "AddSubtask": this.OpenTaskForm(this.State.Selected); break;
            case "OpenTask" when button.DataContext is TaskBoardTaskNode task:
                this.FocusTask(task.TaskId); break;
            case "OpenDependency" when button.DataContext is TaskBoardDependency dependency:
                this.FocusTask(dependency.TaskId); break;
            default: this.Request(action, button.DataContext); break;
        }
    }

    /// <summary>Shows a related task even when the current filter would hide it.</summary>
    public void FocusTask(Guid taskId)
    {
        this.ClearFilters();
        this.Control<ListBox>("TaskList").SelectedItem = this.State.Tasks.FirstOrDefault(task => task.TaskId == taskId);
        this.Control<ListBox>("TaskList").ScrollIntoView(this.Control<ListBox>("TaskList").SelectedItem!);
        this.Control<TabControl>("BoardInspector").SelectedIndex = 0;
    }

    private void ToggleForm(string name, bool visible)
    {
        if (this.busy)
        {
            return;
        }

        this.Control<Border>(name).IsVisible = visible;
        if (visible)
        {
            this.Control<Border>(name == "PlanForm" ? "TaskForm" : "PlanForm").IsVisible = false;
            this.Control<TextBox>(name == "PlanForm" ? "FeatureRequest" : "NewTaskTitle").Focus();
        }
    }

    private void OpenTaskForm(TaskBoardTaskNode? parent)
    {
        if (this.busy || this.State.BoardId is not { } boardId)
        {
            return;
        }

        if (this.entry is null || this.EntryIsEmpty())
        {
            this.entry = new TaskBoardEntry(boardId, parent?.TaskId, Guid.NewGuid(), string.Empty, string.Empty, []);
            this.Control<TextBlock>("TaskFormHeading").Text = parent is null
                ? $"Add a task to {this.State.Title}" : $"Add a subtask to {parent.DisplayTitle}";
        }

        this.ToggleForm("TaskForm", true);
    }

    private bool EntryIsEmpty() => string.IsNullOrWhiteSpace(this.Control<TextBox>("NewTaskTitle").Text)
        && string.IsNullOrWhiteSpace(this.Control<TextBox>("NewTaskDescription").Text)
        && string.IsNullOrWhiteSpace(this.Control<TextBox>("NewTaskCriteria").Text);

    private void SubmitTask()
    {
        if (this.busy || this.entry is null)
        {
            return;
        }

        var title = this.Control<TextBox>("NewTaskTitle").Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            this.State.Message = "Give the task a title before adding it.";
            return;
        }

        var description = this.Control<TextBox>("NewTaskDescription").Text?.Trim() ?? string.Empty;
        var criteria = (this.Control<TextBox>("NewTaskCriteria").Text ?? string.Empty)
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        this.Request("AddTask", this.entry with { Title = title, Description = description, Criteria = criteria });
    }

    private void Request(string action, object? target)
    {
        if (!this.busy)
        {
            this.ActionRequested?.Invoke(action, target);
        }
    }
}
