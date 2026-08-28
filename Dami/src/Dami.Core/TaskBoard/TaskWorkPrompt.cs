using System.Text;
using Dami.Contracts.TaskBoard;

namespace Dami.Core.TaskBoard;

/// <summary>Turns one board task into the request a turn is run against.</summary>
/// <remarks>
/// Pure and static, so the wording is testable without a model or a database.
///
/// The first version of this text spent its final paragraph on prohibitions — you cannot
/// change the board, you must not claim, say what is missing and stop — and an 8B local
/// model read that as an invitation to decline: it answered that it lacked the authority
/// to act and produced nothing. The boundary is enforced in code and in SQL, not by
/// telling the model what it may not do; the prompt's job is to ask for the artifact. So
/// this text now says what to write, asks for a position rather than a survey, and tells
/// the model to reason from a stated assumption instead of stopping at a missing fact.
/// </remarks>
public static class TaskWorkPrompt
{
    /// <summary>Builds the request for one advisory run against a task.</summary>
    public static string Build(string boardTitle, BoardTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(boardTitle);

        var text = new StringBuilder();
        text.Append("You are being asked to work one task from the task board \"")
            .Append(boardTitle).AppendLine("\".").AppendLine();
        text.Append("Task: ").AppendLine(task.Title);
        text.Append("Status: ").Append(task.Status).Append(" · priority ")
            .AppendLine(task.Priority.ToString());

        if (!string.IsNullOrWhiteSpace(task.Description))
        {
            text.AppendLine().AppendLine("Scope as recorded on the board:")
                .AppendLine(task.Description.Trim());
        }

        AppendCriteria(text, task.AcceptanceCriteria);
        text.AppendLine().AppendLine(
            "Write the most useful thing you can for whoever picks this up next. "
            + "Depending on the task that is a recommendation with its reasoning, a "
            + "draft of the decision or document it calls for, the specific trade-offs "
            + "worth weighing, or the questions that have to be answered first — and "
            + "name which of those you are giving. Be concrete and take a position: "
            + "\"I would do X, because Y\" is worth more than a summary of the options. "
            + "Where you are missing a fact, say what you would need and then reason "
            + "from your best assumption anyway, stating it. Someone else records the "
            + "result on the board afterwards, so write the content, not a status "
            + "report about it.");

        return text.ToString();
    }

    private static void AppendCriteria(
        StringBuilder text,
        IReadOnlyList<AcceptanceCriterion> criteria)
    {
        text.AppendLine();
        if (criteria.Count == 0)
        {
            text.AppendLine(
                "This task has no acceptance criteria yet, so nothing gates its "
                + "completion. Proposing the criteria worth gating on is a useful answer.");
            return;
        }

        text.AppendLine("Acceptance criteria, which are what this task is measured against:");
        foreach (var criterion in criteria)
        {
            text.Append("- [").Append(criterion.IsSatisfied ? "satisfied" : "not satisfied")
                .Append("] ").AppendLine(criterion.Description);
        }
    }
}
