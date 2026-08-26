using System.Text.Json;
using System.Text.RegularExpressions;
using Dami.Contracts.TaskBoard;
using Dami.Core.BoardImport;

namespace Dami.Gateway.Cli;

/// <summary>`dami board …` — the collaborative task board (O1), via the runtime API.</summary>
/// <remarks>
/// Tasks and criteria are addressed by the first eight hex characters of their id, the
/// way traces are. Every mutation is guarded server-side by the version the CLI read, so
/// a stale view cannot overwrite a newer one; a 409 is reported as what it is.
/// </remarks>
public sealed partial class BoardCommands
{
    private const int TITLE_WIDTH = 88;

    private readonly DamiApiClient api;
    private readonly TaskActor actor;

    /// <summary>Creates the commands.</summary>
    public BoardCommands(DamiApiClient api, TaskActor actor)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(actor);
        this.api = api;
        this.actor = actor;
    }

    /// <summary>Lists boards, most recently active first.</summary>
    public Task<int> ListAsync(CancellationToken cancellationToken)
    {
        return ApiCall.RunAsync(async () =>
        {
            using var reply = await this.api.GetAsync("/task-boards", cancellationToken)
                .ConfigureAwait(false);
            var any = false;
            foreach (var board in reply!.RootElement.EnumerateArray())
            {
                any = true;
                Console.WriteLine(
                    $"{Id8(board, "boardId")}  {board.GetProperty("status").GetString(),-10}  "
                    + $"{board.GetProperty("doneTasks").GetInt32(),4}/"
                    + $"{board.GetProperty("totalTasks").GetInt32(),-4} done  "
                    + $"{board.GetProperty("blockedTasks").GetInt32(),3} blocked  "
                    + board.GetProperty("title").GetString());
            }

            if (!any)
            {
                Console.WriteLine("no boards - dami board-import <TODO.md> creates the first");
            }

            return 0;
        });
    }

    /// <summary>Prints one board's task tree; <paramref name="openOnly"/> hides finished work.</summary>
    public Task<int> ShowAsync(string boardPrefix, bool openOnly, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(boardPrefix);
        return ApiCall.RunAsync(async () =>
        {
            var boardId = await this.ResolveBoardAsync(boardPrefix, cancellationToken).ConfigureAwait(false);
            if (boardId is null)
            {
                await Console.Error.WriteLineAsync($"no board matches '{boardPrefix}'").ConfigureAwait(false);
                return 1;
            }

            using var reply = await this.api.GetAsync($"/task-boards/{boardId:D}", cancellationToken)
                .ConfigureAwait(false);
            var root = reply!.RootElement;
            Console.WriteLine($"{root.GetProperty("title").GetString()}  [{root.GetProperty("status").GetString()}]");
            Console.WriteLine();
            foreach (var task in root.GetProperty("tasks").EnumerateArray())
            {
                PrintTask(task, 0, openOnly);
            }

            return 0;
        });
    }

    /// <summary>
    /// Adds a task under a task, or at a board's root when <paramref name="target"/> names a
    /// board. It goes last among its siblings; each <paramref name="criteria"/> becomes an
    /// acceptance criterion the completion gate will check.
    /// </summary>
    public Task<int> AddAsync(
        string target, string title, IReadOnlyList<string> criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(criteria);
        var id = TitleIdPattern().Match(title);
        return ApiCall.RunAsync(async () =>
        {
            if (!id.Success)
            {
                await Console.Error.WriteLineAsync(
                    "a task title starts with its id, like `O2g Do the thing` - that id is its identity on the board and in TODO.md")
                    .ConfigureAwait(false);
                return 2;
            }

            var parent = await this.FindAsync(target, criteria: false, cancellationToken).ConfigureAwait(false);
            var boardId = parent?.BoardId ?? await this.ResolveBoardAsync(target, cancellationToken).ConfigureAwait(false);
            if (boardId is null)
            {
                await Console.Error.WriteLineAsync($"no task or board matches '{target}'").ConfigureAwait(false);
                return 1;
            }

            var position = parent?.ChildCount
                ?? await this.RootCountAsync(boardId.Value, cancellationToken).ConfigureAwait(false);
            using var reply = await this.api.PostAsync(
                $"/task-boards/{boardId:D}/tasks",
                this.AddBody(title, TodoBoardMapper.StableTaskId(boardId.Value, id.Value), parent?.Id, position, criteria),
                cancellationToken).ConfigureAwait(false);
            return await ReportAddedAsync(reply, title, parent?.Title).ConfigureAwait(false);
        });
    }

    private object AddBody(string title, Guid? taskId, Guid? parentTaskId, int position, IReadOnlyList<string> criteria)
    {
        return new
        {
            title,
            taskId,
            parentTaskId,
            position,
            criteria,
            actorId = this.actor.ActorId,
            actorKind = this.actor.Kind.ToString(),
        };
    }

    private static async Task<int> ReportAddedAsync(JsonDocument? reply, string title, string? parentTitle)
    {
        if (reply is null)
        {
            await Console.Error.WriteLineAsync(
                "the runtime has no add-task endpoint for that board - is dami-host older than this CLI?")
                .ConfigureAwait(false);
            return 1;
        }

        Console.WriteLine($"added {reply.RootElement.GetProperty("taskId").GetGuid().ToString("N")[..8]}: {Shorten(title)}"
            + (parentTitle is null ? string.Empty : $"  under {Shorten(parentTitle)}"));
        return 0;
    }

    /// <summary>Prints the board in TODO.md's grammar, so the file can be derived from it.</summary>
    public Task<int> ExportAsync(string boardPrefix, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(boardPrefix);
        return ApiCall.RunAsync(async () =>
        {
            var boardId = await this.ResolveBoardAsync(boardPrefix, cancellationToken).ConfigureAwait(false);
            if (boardId is null)
            {
                await Console.Error.WriteLineAsync($"no board matches '{boardPrefix}'").ConfigureAwait(false);
                return 1;
            }

            using var reply = await this.api.GetAsync($"/task-boards/{boardId:D}", cancellationToken)
                .ConfigureAwait(false);
            var board = reply!.RootElement.Deserialize<TaskBoardSnapshot>(snapshotJson)
                ?? throw new DamiRuntimeException("the board could not be read as a snapshot");
            Console.Write(TodoBoardRenderer.Render(board));
            return 0;
        });
    }

    private static readonly JsonSerializerOptions snapshotJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    [GeneratedRegex(@"^[A-Z]\d+[a-z0-9]*\b")]
    private static partial Regex TitleIdPattern();

    private async Task<int> RootCountAsync(Guid boardId, CancellationToken cancellationToken)
    {
        using var snapshot = await this.api.GetAsync($"/task-boards/{boardId:D}", cancellationToken)
            .ConfigureAwait(false);
        return snapshot!.RootElement.GetProperty("tasks").GetArrayLength();
    }

    /// <summary>Claims a task as the configured actor.</summary>
    public Task<int> ClaimAsync(string taskPrefix, string? detail, CancellationToken cancellationToken)
    {
        return this.MutateTaskAsync(taskPrefix, "claim", found => this.api.MutateAsync(
            HttpMethod.Post, $"/task-boards/tasks/{found.Id:D}/claim",
            new { expectedVersion = found.Version, actorId = this.actor.ActorId, actorKind = this.actor.Kind.ToString(), detail },
            cancellationToken), cancellationToken);
    }

    /// <summary>Completes a task the configured actor holds.</summary>
    public Task<int> CompleteAsync(string taskPrefix, string? detail, CancellationToken cancellationToken)
    {
        return this.MutateTaskAsync(taskPrefix, "complete", found => this.api.MutateAsync(
            HttpMethod.Post, $"/task-boards/tasks/{found.Id:D}/complete",
            new { expectedVersion = found.Version, actorId = this.actor.ActorId, actorKind = this.actor.Kind.ToString(), detail },
            cancellationToken), cancellationToken);
    }

    /// <summary>Blocks, reopens, or cancels a task with a stated reason.</summary>
    public Task<int> SetStatusAsync(
        string taskPrefix, TaskBoardStatus status, string detail, CancellationToken cancellationToken)
    {
        return this.MutateTaskAsync(taskPrefix, status.ToString().ToLowerInvariant(), found => this.api.MutateAsync(
            HttpMethod.Put, $"/task-boards/tasks/{found.Id:D}/status",
            new { expectedVersion = found.Version, status = status.ToString(), detail, actorId = this.actor.ActorId, actorKind = this.actor.Kind.ToString() },
            cancellationToken), cancellationToken);
    }

    /// <summary>Marks an acceptance criterion satisfied or not.</summary>
    public Task<int> SetCriterionAsync(string criterionPrefix, bool satisfied, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criterionPrefix);
        return ApiCall.RunAsync(async () =>
        {
            var found = await this.FindAsync(criterionPrefix, criteria: true, cancellationToken).ConfigureAwait(false);
            if (found is null)
            {
                await Console.Error.WriteLineAsync($"no criterion matches '{criterionPrefix}'").ConfigureAwait(false);
                return 1;
            }

            var updated = await this.api.MutateAsync(
                HttpMethod.Put, $"/task-boards/criteria/{found.Id:D}",
                new { expectedVersion = found.Version, isSatisfied = satisfied, actorId = this.actor.ActorId, actorKind = this.actor.Kind.ToString() },
                cancellationToken).ConfigureAwait(false);
            return Report(updated, satisfied ? "satisfied" : "reopened", found.Title);
        });
    }

    private Task<int> MutateTaskAsync(
        string taskPrefix,
        string verb,
        Func<Located, Task<bool?>> mutate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(taskPrefix);
        return ApiCall.RunAsync(async () =>
        {
            var found = await this.FindAsync(taskPrefix, criteria: false, cancellationToken).ConfigureAwait(false);
            if (found is null)
            {
                await Console.Error.WriteLineAsync($"no task matches '{taskPrefix}'").ConfigureAwait(false);
                return 1;
            }

            var updated = await mutate(found).ConfigureAwait(false);
            return Report(updated, verb, found.Title);
        });
    }

    private static int Report(bool? updated, string verb, string title)
    {
        switch (updated)
        {
            case true:
                Console.WriteLine($"{verb}: {Shorten(title)}");
                return 0;
            case false:
                Console.Error.WriteLine(
                    $"conflict: the board refused to {verb} '{Shorten(title)}' - the version moved, "
                    + "or a gate held (prerequisite not done, not the claimant, criteria or children open)");
                return 1;
            default:
                Console.Error.WriteLine("the runtime no longer knows that id");
                return 1;
        }
    }

    private async Task<Guid?> ResolveBoardAsync(string prefix, CancellationToken cancellationToken)
    {
        using var reply = await this.api.GetAsync("/task-boards", cancellationToken).ConfigureAwait(false);
        Guid? match = null;
        foreach (var board in reply!.RootElement.EnumerateArray())
        {
            var id = board.GetProperty("boardId").GetGuid();
            var title = board.GetProperty("title").GetString() ?? string.Empty;
            if (id.ToString("N").StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                if (match is not null)
                {
                    return null;
                }

                match = id;
            }
        }

        return match;
    }

    /// <summary>Finds a task (or criterion) by id prefix across every board. Ambiguity is a miss.</summary>
    private async Task<Located?> FindAsync(string prefix, bool criteria, CancellationToken cancellationToken)
    {
        using var boards = await this.api.GetAsync("/task-boards", cancellationToken).ConfigureAwait(false);
        var matches = new List<Located>();
        foreach (var board in boards!.RootElement.EnumerateArray())
        {
            var boardId = board.GetProperty("boardId").GetGuid();
            using var snapshot = await this.api.GetAsync($"/task-boards/{boardId:D}", cancellationToken)
                .ConfigureAwait(false);
            foreach (var task in snapshot!.RootElement.GetProperty("tasks").EnumerateArray())
            {
                Collect(task, boardId, prefix, criteria, matches);
            }
        }

        return matches.Count == 1 ? matches[0] : null;
    }

    private static void Collect(JsonElement task, Guid boardId, string prefix, bool criteria, List<Located> matches)
    {
        var version = task.GetProperty("version").GetInt64();
        var children = task.GetProperty("subTasks");
        if (!criteria && Matches(task, "taskId", prefix))
        {
            matches.Add(new Located(
                task.GetProperty("taskId").GetGuid(), version, Title(task), boardId, children.GetArrayLength()));
        }

        if (criteria)
        {
            foreach (var criterion in task.GetProperty("acceptanceCriteria").EnumerateArray())
            {
                if (Matches(criterion, "criterionId", prefix))
                {
                    matches.Add(new Located(
                        criterion.GetProperty("criterionId").GetGuid(), version,
                        criterion.GetProperty("description").GetString() ?? string.Empty, boardId, 0));
                }
            }
        }

        foreach (var child in children.EnumerateArray())
        {
            Collect(child, boardId, prefix, criteria, matches);
        }
    }

    private static void PrintTask(JsonElement task, int depth, bool openOnly)
    {
        var status = task.GetProperty("status").GetString() ?? string.Empty;
        var finished = status is "Done" or "Cancelled";
        if (!openOnly || !finished || HasOpenDescendant(task))
        {
            var claim = task.GetProperty("claim");
            var holder = claim.ValueKind == JsonValueKind.Object
                ? "  @" + claim.GetProperty("actor").GetProperty("actorId").GetString()
                : string.Empty;
            Console.WriteLine(
                $"{Id8(task, "taskId")}  {Mark(status)} {new string(' ', depth * 2)}{Shorten(Title(task))}{holder}");
            foreach (var criterion in task.GetProperty("acceptanceCriteria").EnumerateArray())
            {
                var satisfied = criterion.GetProperty("isSatisfied").GetBoolean();
                Console.WriteLine(
                    $"{Id8(criterion, "criterionId")}  {(satisfied ? "[✓]" : "[ ]")} {new string(' ', depth * 2 + 2)}"
                    + $"criterion: {criterion.GetProperty("description").GetString()}");
            }
        }

        foreach (var child in task.GetProperty("subTasks").EnumerateArray())
        {
            PrintTask(child, depth + 1, openOnly);
        }
    }

    private static bool HasOpenDescendant(JsonElement task)
    {
        foreach (var child in task.GetProperty("subTasks").EnumerateArray())
        {
            var status = child.GetProperty("status").GetString();
            if (status is not ("Done" or "Cancelled") || HasOpenDescendant(child))
            {
                return true;
            }
        }

        return false;
    }

    private static string Mark(string status)
    {
        return status switch
        {
            "Done" => "[x]",
            "InProgress" => "[~]",
            "Blocked" => "[!]",
            "Cancelled" => "[-]",
            _ => "[ ]",
        };
    }

    private static bool Matches(JsonElement element, string property, string prefix)
    {
        return element.GetProperty(property).GetGuid().ToString("N")
            .StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string Id8(JsonElement element, string property)
    {
        return element.GetProperty(property).GetGuid().ToString("N")[..8];
    }

    private static string Title(JsonElement task)
    {
        return task.GetProperty("title").GetString() ?? string.Empty;
    }

    private static string Shorten(string title)
    {
        return title.Length <= TITLE_WIDTH ? title : title[..(TITLE_WIDTH - 1)] + "…";
    }

    private sealed record Located(Guid Id, long Version, string Title, Guid BoardId, int ChildCount);
}
