using Avalonia.Controls;

namespace Dami.Gui;

public sealed partial class TaskBoardWorkspace
{
    private readonly Dictionary<Guid, (string Requirement, string Reason)> inspectorDrafts = [];
    private Guid? inspectorTaskId;

    private void RestoreInspectorDraft()
    {
        var selectedId = this.State.Selected?.TaskId;
        if (this.inspectorTaskId == selectedId)
        {
            return;
        }

        var requirement = this.Control<TextBox>("NewCriterion");
        var reason = this.Control<TextBox>("StatusDetail");
        if (this.inspectorTaskId is { } previous)
        {
            this.inspectorDrafts[previous] = (requirement.Text ?? string.Empty, reason.Text ?? string.Empty);
        }

        this.inspectorTaskId = selectedId;
        var draft = selectedId is { } id && this.inspectorDrafts.TryGetValue(id, out var saved)
            ? saved : (Requirement: string.Empty, Reason: string.Empty);
        requirement.Text = draft.Requirement;
        reason.Text = draft.Reason;
    }
}
