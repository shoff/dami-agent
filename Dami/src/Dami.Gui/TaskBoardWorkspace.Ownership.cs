using Avalonia.Controls;
using Dami.Contracts.TaskBoard;

namespace Dami.Gui;

public sealed partial class TaskBoardWorkspace
{
    private void RenderOwnership()
    {
        var actor = this.Control<TextBox>("BoardActor").Text?.Trim() ?? string.Empty;
        var kind = this.Control<ComboBox>("ActorKindPicker").SelectedIndex == 1 ? TaskActorKind.Agent : TaskActorKind.Human;
        var task = this.State.Selected;
        var owns = task?.ClaimedBy == actor && task.ClaimedKind == kind;
        this.Control<Button>("CompleteTask").IsEnabled = task?.CanComplete == true && owns;
        this.Control<Button>("BlockTask").IsEnabled = owns;
        var notice = this.Control<TextBlock>("OwnerNotice");
        notice.IsVisible = task?.CanWork == true && !owns;
        notice.Text = notice.IsVisible ? $"{task!.ClaimedBy} ({task.ClaimedKind}) owns this task. Only its owner can complete or change its active status." : string.Empty;
        this.Control<Expander>("ActorSettings").Header = $"Acting as {actor}";
    }
}
