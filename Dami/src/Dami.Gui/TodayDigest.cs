using System.Text.Json;

namespace Dami.Gui;

/// <summary>The morning digest, from the runtime's own JSON: pure, so it tests without a window.</summary>
public static class TodayDigest
{
    private const int LOOKAHEAD_DAYS = 7;

    /// <summary>Blocked tasks whose reason names Steve — the board's questions for him.</summary>
    public static List<SidebarItem> BoardQuestions(JsonElement tasks)
    {
        var questions = new List<SidebarItem>();
        foreach (var task in tasks.EnumerateArray())
        {
            Walk(task, questions);
        }

        return questions;
    }

    /// <summary>Civic meetings within the coming week, soonest first.</summary>
    public static List<SidebarItem> CivicWeek(JsonElement facts, DateOnly today)
    {
        return facts.EnumerateArray()
            .Where(fact => fact.GetProperty("category").GetString() == "meeting")
            .Select(fact => (Date: DateOnly.Parse(fact.GetProperty("asOf").GetString()!), Fact: fact))
            .Where(item => item.Date >= today && item.Date <= today.AddDays(LOOKAHEAD_DAYS))
            .OrderBy(item => item.Date)
            .Select(item => new SidebarItem(
                Id8(item.Fact, "factId"),
                $"CIVIC · {item.Date:ddd MM-dd} · {Before(item.Fact.GetProperty("description").GetString()!, " — ")}",
                item.Fact.GetProperty("source").GetString() ?? string.Empty))
            .ToList();
    }

    /// <summary>Only what is wrong in the latest network pass; a healthy network adds nothing.</summary>
    public static List<SidebarItem> NetworkProblems(JsonElement facts)
    {
        var all = facts.EnumerateArray().ToList();
        if (all.Count == 0)
        {
            return [];
        }

        var latest = all[0].GetProperty("asOf").GetString();
        return all
            .Where(fact => fact.GetProperty("asOf").GetString() == latest)
            .Where(fact => IsProblem(fact.GetProperty("description").GetString()!))
            .Select(fact => new SidebarItem(
                Id8(fact, "factId"), "NETWORK · " + fact.GetProperty("description").GetString(), $"as of {latest}"))
            .ToList();
    }

    private static void Walk(JsonElement task, List<SidebarItem> questions)
    {
        if (task.GetProperty("status").GetString() == "Blocked"
            && (task.GetProperty("description").GetString() ?? string.Empty).Contains("STEVE", StringComparison.Ordinal))
        {
            var id = Id8(task, "taskId");
            questions.Add(new SidebarItem(
                id, "YOURS · " + task.GetProperty("title").GetString(), $"{id} · dami board reopen {id} \"…\""));
        }

        foreach (var child in task.GetProperty("subTasks").EnumerateArray())
        {
            Walk(child, questions);
        }
    }

    private static bool IsProblem(string description)
    {
        return description.Contains("not listening", StringComparison.Ordinal)
            || description.Contains("does not answer", StringComparison.Ordinal);
    }

    private static string Id8(JsonElement element, string property)
    {
        return element.GetProperty(property).GetGuid().ToString("N")[..8];
    }

    private static string Before(string text, string separator)
    {
        var index = text.IndexOf(separator, StringComparison.Ordinal);
        return index < 0 ? text : text[..index];
    }
}
