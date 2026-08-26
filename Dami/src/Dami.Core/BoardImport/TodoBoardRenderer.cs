using System.Text;
using System.Text.RegularExpressions;
using Dami.Contracts.TaskBoard;

namespace Dami.Core.BoardImport;

/// <summary>Writes a board back in TODO.md's grammar, so the file can be derived from the board.</summary>
/// <remarks>
/// The inverse of the reader for everything the grammar can say: sections from root tasks
/// titled <c>K · Name</c>, one checklist line per task with the marker its status denotes,
/// the claim as <c>[~ Owner date]</c>, a blocked reason as the trailing annotation the file
/// already uses, prerequisites as "(needs X first)", and "acceptance item N" criteria as the
/// suffix the reader recognises. What the grammar cannot say — a root without a section key,
/// a task whose title carries no id, a free-text criterion — is written as an HTML comment
/// the reader ignores, never invented into a task. Reading the output back yields the same
/// tree, which is the test.
/// </remarks>
public static partial class TodoBoardRenderer
{
    /// <summary>Renders the board.</summary>
    public static string Render(TaskBoardSnapshot board)
    {
        ArgumentNullException.ThrowIfNull(board);
        var text = new StringBuilder();
        text.Append("# ").Append(board.Title).AppendLine(" — rendered from the task board");
        text.AppendLine();
        text.Append("<!-- board ").Append(board.BoardId.ToString("D"))
            .AppendLine("; written by `dami board export`. The board is authoritative: edit it there. -->");
        var byId = new Dictionary<Guid, BoardTask>();
        Index(board.Tasks, byId);
        foreach (var root in board.Tasks.OrderBy(task => task.Position))
        {
            text.AppendLine();
            RenderRoot(root, byId, text);
        }

        return text.ToString();
    }

    private static void Index(IReadOnlyList<BoardTask> tasks, Dictionary<Guid, BoardTask> byId)
    {
        foreach (var task in tasks)
        {
            byId[task.TaskId] = task;
            Index(task.SubTasks, byId);
        }
    }

    private static void RenderRoot(BoardTask root, IReadOnlyDictionary<Guid, BoardTask> byId, StringBuilder text)
    {
        var heading = HeadingPattern().Match(root.Title);
        if (!heading.Success)
        {
            text.Append("<!-- root without a section key, not rendered: ").Append(Id8(root))
                .Append(' ').Append(root.Title).AppendLine(" -->");
            return;
        }

        text.Append("## ").Append(heading.Groups[1].Value).Append(" · ").AppendLine(heading.Groups[2].Value);
        text.AppendLine();
        foreach (var task in root.SubTasks.OrderBy(task => task.Position))
        {
            RenderTask(task, 0, byId, text);
        }
    }

    private static void RenderTask(
        BoardTask task, int depth, IReadOnlyDictionary<Guid, BoardTask> byId, StringBuilder text)
    {
        var indent = new string(' ', depth * 2);
        if (!IdPattern().IsMatch(task.Title))
        {
            text.Append(indent).Append("<!-- task without an id, not rendered: ").Append(Id8(task))
                .Append(' ').Append(task.Title).AppendLine(" -->");
            return;
        }

        text.Append(indent).Append("- ").Append(Marker(task)).Append(' ').Append(task.Title)
            .Append(Suffixes(task)).Append(PrerequisitePhrase(task, byId)).AppendLine();
        foreach (var criterion in task.AcceptanceCriteria.OrderBy(criterion => criterion.Position))
        {
            if (!AcceptanceItemPattern().IsMatch(criterion.Description))
            {
                text.Append(indent).Append("  <!-- criterion").Append(criterion.IsSatisfied ? " [x]: " : ": ")
                    .Append(criterion.Description).AppendLine(" -->");
            }
        }

        foreach (var child in task.SubTasks.OrderBy(child => child.Position))
        {
            RenderTask(child, depth + 1, byId, text);
        }
    }

    /// <summary>The marker the status denotes; blocked keeps the file's own annotation form.</summary>
    private static string Marker(BoardTask task)
    {
        return task.Status switch
        {
            TaskBoardStatus.Done => "[x]",
            TaskBoardStatus.Cancelled => "[-]",
            TaskBoardStatus.InProgress when task.Claim is { } claim
                => $"[~ {Capitalize(claim.Actor.ActorId)} {claim.ClaimedAt.UtcDateTime:yyyy-MM-dd}]",
            TaskBoardStatus.Blocked when LeadingMarker(task) == "DEFERRED"
                => $"[DEFERRED: {Reason(task, "Marker detail: ") ?? "no reason recorded"}]",
            TaskBoardStatus.Blocked when LeadingMarker(task) == "STEVE" || TrailingSteve(task)
                => "[STEVE]",
            _ => "[ ]",
        };
    }

    private static string Suffixes(BoardTask task)
    {
        var suffix = new StringBuilder();
        if (task.Status == TaskBoardStatus.Blocked && !Marker(task).StartsWith("[STEVE", StringComparison.Ordinal)
            && !Marker(task).StartsWith("[DEFERRED", StringComparison.Ordinal))
        {
            suffix.Append(" `[BLOCKED: ").Append(Reason(task, "Blocked: ") ?? "see the board").Append("]`");
        }

        foreach (var criterion in task.AcceptanceCriteria.OrderBy(criterion => criterion.Position))
        {
            // An imported title already says "acceptance item N"; saying it twice makes two.
            if (AcceptanceItemPattern().IsMatch(criterion.Description)
                && !task.Title.Contains(criterion.Description, StringComparison.OrdinalIgnoreCase))
            {
                suffix.Append(" — ").Append(criterion.Description);
            }
        }

        return suffix.ToString();
    }

    /// <summary>"(needs X first)" is the one dependency phrase the reader resolves.</summary>
    private static string PrerequisitePhrase(BoardTask task, IReadOnlyDictionary<Guid, BoardTask> byId)
    {
        var phrases = new StringBuilder();
        foreach (var prerequisiteId in task.PrerequisiteTaskIds)
        {
            if (byId.TryGetValue(prerequisiteId, out var prerequisite)
                && IdPattern().Match(prerequisite.Title) is { Success: true } id)
            {
                phrases.Append(" (needs ").Append(id.Value).Append(" first)");
            }
        }

        return phrases.ToString();
    }

    private static string? Reason(BoardTask task, string label)
    {
        foreach (var line in task.Description.Split('\n'))
        {
            if (line.StartsWith(label, StringComparison.Ordinal))
            {
                return line[label.Length..].Trim();
            }
        }

        return null;
    }

    /// <summary>The keyword of the marker the imported line led with, if it led with one.</summary>
    private static string? LeadingMarker(BoardTask task)
    {
        var match = LeadingMarkerPattern().Match(FirstLine(task.Description));
        return match.Success ? match.Groups[1].Value : null;
    }

    private static bool TrailingSteve(BoardTask task)
    {
        return TrailingStevePattern().IsMatch(FirstLine(task.Description));
    }

    private static string FirstLine(string description)
    {
        var end = description.IndexOf('\n', StringComparison.Ordinal);
        return end < 0 ? description : description[..end];
    }

    private static string Capitalize(string actorId)
    {
        return actorId.Length == 0 ? actorId : char.ToUpperInvariant(actorId[0]) + actorId[1..];
    }

    private static string Id8(BoardTask task)
    {
        return task.TaskId.ToString("N")[..8];
    }

    [GeneratedRegex(@"^([A-Z])\s+·\s+(.+)$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^[A-Z]\d+[a-z0-9]*\b")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^acceptance item \d+$", RegexOptions.IgnoreCase)]
    private static partial Regex AcceptanceItemPattern();

    [GeneratedRegex(@"^\s*- \[(DEFERRED|STEVE)\b")]
    private static partial Regex LeadingMarkerPattern();

    [GeneratedRegex(@"`?\[STEVE:[^\]]*\]`?\s*$")]
    private static partial Regex TrailingStevePattern();
}
