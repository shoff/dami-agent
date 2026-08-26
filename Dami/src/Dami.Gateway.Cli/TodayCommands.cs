using System.Text.Json;

namespace Dami.Gateway.Cli;

/// <summary>`dami today` — the one screen to read in the morning.</summary>
/// <remarks>
/// Nothing new is computed here: it is the inbox, the board's questions for Steve, the
/// civic calendar for the week, and the network facts that changed for the worse, each
/// already served by the runtime, read together. A section with nothing to say says so
/// in one line rather than vanishing, so an empty day reads as quiet, not broken.
/// </remarks>
public sealed class TodayCommands
{
    private const int LOOKAHEAD_DAYS = 7;
    private const int MAX_LINES = 6;

    private readonly DamiApiClient api;
    private readonly TimeProvider clock;

    /// <summary>Creates the commands.</summary>
    public TodayCommands(DamiApiClient api, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(clock);
        this.api = api;
        this.clock = clock;
    }

    /// <summary>Prints the day.</summary>
    public Task<int> ShowAsync(CancellationToken cancellationToken)
    {
        return ApiCall.RunAsync(async () =>
        {
            var today = DateOnly.FromDateTime(this.clock.GetLocalNow().DateTime);
            Console.WriteLine($"Dami · {today:dddd yyyy-MM-dd}");
            await this.InboxAsync(cancellationToken).ConfigureAwait(false);
            await this.BoardAsync(cancellationToken).ConfigureAwait(false);
            await this.CivicAsync(today, cancellationToken).ConfigureAwait(false);
            await this.NetworkAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        });
    }

    private async Task InboxAsync(CancellationToken cancellationToken)
    {
        using var reply = await this.api.GetAsync("/surfacings", cancellationToken).ConfigureAwait(false);
        var items = reply!.RootElement.EnumerateArray().ToList();
        Section("inbox", items.Count == 0 ? "quiet" : $"{items.Count} pending — dami inbox");
        foreach (var item in items.Take(MAX_LINES))
        {
            Console.WriteLine($"  {item.GetProperty("surfacingId").GetGuid().ToString("N")[..8]}  {item.GetProperty("title").GetString()}");
        }
    }

    /// <summary>Blocked tasks whose reason names Steve are his questions, not the agents'.</summary>
    private async Task BoardAsync(CancellationToken cancellationToken)
    {
        using var boards = await this.api.GetAsync("/task-boards", cancellationToken).ConfigureAwait(false);
        var waiting = new List<string>();
        var held = 0;
        foreach (var board in boards!.RootElement.EnumerateArray())
        {
            using var snapshot = await this.api.GetAsync(
                $"/task-boards/{board.GetProperty("boardId").GetGuid():D}", cancellationToken).ConfigureAwait(false);
            foreach (var task in snapshot!.RootElement.GetProperty("tasks").EnumerateArray())
            {
                held += Walk(task, waiting);
            }
        }

        Section("board", $"{held} task(s) in progress · {waiting.Count} waiting on you — dami board dami --open");
        foreach (var line in waiting.Take(MAX_LINES))
        {
            Console.WriteLine($"  {line}");
        }
    }

    private static int Walk(JsonElement task, List<string> waiting)
    {
        var status = task.GetProperty("status").GetString();
        var held = status == "InProgress" ? 1 : 0;
        if (status == "Blocked" && task.GetProperty("description").GetString()!.Contains("STEVE", StringComparison.Ordinal))
        {
            var title = task.GetProperty("title").GetString() ?? string.Empty;
            waiting.Add($"{task.GetProperty("taskId").GetGuid().ToString("N")[..8]}  {(title.Length > 80 ? title[..79] + "…" : title)}");
        }

        foreach (var child in task.GetProperty("subTasks").EnumerateArray())
        {
            held += Walk(child, waiting);
        }

        return held;
    }

    private async Task CivicAsync(DateOnly today, CancellationToken cancellationToken)
    {
        using var reply = await this.api.GetAsync("/domains/civic", cancellationToken).ConfigureAwait(false);
        var meetings = reply!.RootElement.EnumerateArray()
            .Where(fact => fact.GetProperty("category").GetString() == "meeting")
            .Select(fact => (Date: DateOnly.Parse(fact.GetProperty("asOf").GetString()!), Text: fact.GetProperty("description").GetString()!))
            .Where(fact => fact.Date >= today && fact.Date <= today.AddDays(LOOKAHEAD_DAYS))
            .OrderBy(fact => fact.Date)
            .ToList();
        Section("civic", meetings.Count == 0 ? "no meetings in the next week" : $"{meetings.Count} meeting(s) this week");
        foreach (var (date, text) in meetings.Take(MAX_LINES))
        {
            Console.WriteLine($"  {date:ddd MM-dd}  {Before(text, " — ")}");
        }
    }

    /// <summary>Only what is wrong: a healthy network is one line.</summary>
    private async Task NetworkAsync(CancellationToken cancellationToken)
    {
        using var reply = await this.api.GetAsync("/domains/network", cancellationToken).ConfigureAwait(false);
        var facts = reply!.RootElement.EnumerateArray().ToList();
        if (facts.Count == 0)
        {
            Section("network", "not collected yet");
            return;
        }

        var latest = facts[0].GetProperty("asOf").GetString();
        var wrong = facts
            .Where(fact => fact.GetProperty("asOf").GetString() == latest)
            .Select(fact => fact.GetProperty("description").GetString()!)
            .Where(text => text.Contains("not listening", StringComparison.Ordinal) || text.Contains("does not answer", StringComparison.Ordinal))
            .ToList();
        Section("network", wrong.Count == 0 ? $"all good as of {latest}" : $"{wrong.Count} problem(s) as of {latest}");
        foreach (var line in wrong.Take(MAX_LINES))
        {
            Console.WriteLine($"  {line}");
        }
    }

    private static void Section(string name, string summary)
    {
        Console.WriteLine();
        Console.WriteLine($"{name,-8} {summary}");
    }

    private static string Before(string text, string separator)
    {
        var index = text.IndexOf(separator, StringComparison.Ordinal);
        return index < 0 ? text : text[..index];
    }
}
