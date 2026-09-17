using Avalonia.Controls;

namespace Dami.Gui;

public sealed partial class TaskBoardWorkspace
{
    private readonly Dictionary<Guid, (string Answer, string Status)> advice = [];

    /// <summary>Keeps advice tied to its task even when the user selects another one.</summary>
    public void ShowAdvice(Guid taskId, string answer, string status)
    {
        this.advice[taskId] = (answer, status);
        this.RenderAdvice();
    }

    private void RenderAdvice()
    {
        var content = this.State.Selected is { } task && this.advice.TryGetValue(task.TaskId, out var saved)
            ? saved : (Answer: string.Empty, Status: string.Empty);
        this.Control<TextBlock>("AdviceStatus").Text = content.Status;
        this.Control<ReplyView>("AdviceAnswer").Text = content.Answer;
    }
}
