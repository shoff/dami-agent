using Dami.Contracts.TaskBoard;
using Dami.Core.BoardImport;
using Microsoft.Extensions.Logging;

namespace Dami.Gateway.Cli;

/// <summary>`dami board-import &lt;TODO.md&gt;` — write the blueprint onto the task board (O1g).</summary>
/// <remarks>
/// The third direct-database exception to D-005, beside `health` and `caption`: the file
/// lives in the repository, which the deployed Host cannot see, and the run is an operator's
/// deliberate act rather than a turn. Every rerun is safe — the importer advances only and
/// reports, never forces, what the board already knows better.
/// </remarks>
public sealed class BoardImportCommands
{
    private const string USAGE = """
        dami board-import <TODO.md> --revision <sha> --actor <id> [--agent] [--dry-run]
          --revision  the commit the file was read at; recorded on every mutation
          --actor     who is running the import; a human unless --agent is given
          --dry-run   parse and plan, print the report, write nothing
        """;

    private readonly ITaskBoardStore store;
    private readonly TimeProvider clock;
    private readonly ILoggerFactory loggers;

    /// <summary>Creates the commands.</summary>
    public BoardImportCommands(ITaskBoardStore store, TimeProvider clock, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(loggers);
        this.store = store;
        this.clock = clock;
        this.loggers = loggers;
    }

    /// <summary>Runs one import. <paramref name="args"/> starts at the file path.</summary>
    public async Task<int> ImportAsync(string[] args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);

        var options = ImportOptions.Parse(args);
        if (options is null)
        {
            Console.WriteLine(USAGE);
            return 2;
        }

        if (!File.Exists(options.Path))
        {
            await Console.Error.WriteLineAsync($"no such file: {options.Path}").ConfigureAwait(false);
            return 1;
        }

        var plan = await this.PlanAsync(options, cancellationToken).ConfigureAwait(false);
        PrintPlan(plan, options);
        if (options.DryRun)
        {
            Console.WriteLine("dry run: nothing written");
            return 0;
        }

        return await this.WriteAsync(plan, options, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TodoImportPlan> PlanAsync(ImportOptions options, CancellationToken cancellationToken)
    {
        var markdown = await File.ReadAllTextAsync(options.Path, cancellationToken).ConfigureAwait(false);
        return TodoBoardMapper.Map(
            TodoBoardParser.Parse(markdown),
            new TodoImportSource(options.Revision, FEATURE_REQUEST, PLAN_DESCRIPTION),
            options.Actor,
            this.clock.GetUtcNow());
    }

    private async Task<int> WriteAsync(
        TodoImportPlan plan, ImportOptions options, CancellationToken cancellationToken)
    {
        var importer = new TodoBoardImporter(
            this.store, this.clock, this.loggers.CreateLogger<TodoBoardImporter>());
        var report = await importer.ImportAsync(plan, options.Actor, options.Revision, cancellationToken)
            .ConfigureAwait(false);
        PrintReport(report);
        return report.Conflicts.Count == 0 ? 0 : 1;
    }

    private const string FEATURE_REQUEST = "The Dami Core end state, as TODO.md states it.";
    private const string PLAN_DESCRIPTION = "Imported from TODO.md by dami board-import.";

    private static void PrintPlan(TodoImportPlan plan, ImportOptions options)
    {
        Console.WriteLine($"board:       {plan.Draft.BoardId}");
        Console.WriteLine($"revision:    {options.Revision}");
        Console.WriteLine($"actor:       {options.Actor.ActorId} ({options.Actor.Kind})");
        Console.WriteLine($"epics:       {plan.Draft.Tasks.Count}");
        Console.WriteLine($"tasks:       {plan.Desired.Count}");
        Console.WriteLine($"anomalies:   {plan.Anomalies.Count}");
        foreach (var anomaly in plan.Anomalies)
        {
            Console.WriteLine($"  line {anomaly.LineNumber}: {anomaly.Reason}");
        }
    }

    private static void PrintReport(TodoImportReport report)
    {
        Console.WriteLine();
        Console.WriteLine(report.BoardCreated ? "board created" : "board already existed");
        Console.WriteLine($"tasks held:  {report.TasksWritten}");
        Console.WriteLine($"mutations:   {report.MutationsApplied}");
        Console.WriteLine($"conflicts:   {report.Conflicts.Count}");
        foreach (var conflict in report.Conflicts)
        {
            Console.WriteLine($"  {conflict}");
        }
    }

    /// <summary>The parsed command line, or null when it is not usable.</summary>
    private sealed record ImportOptions(string Path, string Revision, TaskActor Actor, bool DryRun)
    {
        public static ImportOptions? Parse(string[] args)
        {
            if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
            {
                return null;
            }

            var flags = Flags.Parse(args.AsSpan(1));
            if (flags is null
                || string.IsNullOrWhiteSpace(flags.Revision)
                || string.IsNullOrWhiteSpace(flags.ActorId))
            {
                return null;
            }

            var kind = flags.Agent ? TaskActorKind.Agent : TaskActorKind.Human;
            return new ImportOptions(
                args[0], flags.Revision, new TaskActor(flags.ActorId, kind), flags.DryRun);
        }
    }

    /// <summary>The flags after the path; null when one is unknown or missing its value.</summary>
    private sealed record Flags(string? Revision, string? ActorId, bool Agent, bool DryRun)
    {
        public static Flags? Parse(ReadOnlySpan<string> args)
        {
            var flags = new Flags(null, null, false, false);
            for (var index = 0; index < args.Length; index++)
            {
                var hasValue = index + 1 < args.Length;
                flags = args[index] switch
                {
                    "--revision" when hasValue => flags with { Revision = args[++index] },
                    "--actor" when hasValue => flags with { ActorId = args[++index] },
                    "--agent" => flags with { Agent = true },
                    "--dry-run" => flags with { DryRun = true },
                    _ => null,
                };
                if (flags is null)
                {
                    return null;
                }
            }

            return flags;
        }
    }
}
