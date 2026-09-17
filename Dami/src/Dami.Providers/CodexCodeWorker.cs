using System.Globalization;
using System.Text;
using Dami.Contracts.Code;
using Dami.Contracts.Events;
using Dami.Contracts.Privacy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Providers;

/// <summary>Changes this host's own code through the Codex subscription, one branch per task (ADR-0036).</summary>
/// <remarks>
/// The same shape as <see cref="CodexSubscriptionImageGenerator"/>: a <c>codex exec</c>
/// in a <c>workspace-write</c> sandbox, bounded, refusable, and recorded. The sandbox is
/// a fresh <c>git worktree</c> on a stamped branch under a directory of its own, never the
/// repository's main working tree. Afterwards this host — outside any sandbox — runs the
/// build itself for the receipt, commits on the branch, and reports. It never merges,
/// pushes, or deploys; the worktree stays for Steve to look at.
/// </remarks>
public sealed class CodexCodeWorker : ICodeWorker
{
    private const string ACTOR = "code-codex-subscription";
    private const int SLUG_LENGTH = 40;
    private const int SUBJECT_LENGTH = 72;
    private static readonly TimeSpan gitTimeout = TimeSpan.FromSeconds(60);

    private readonly ICodexProcess codex;
    private readonly ICommandRunner commands;
    private readonly CodexOptions codexOptions;
    private readonly CodeWorkOptions options;
    private readonly IEgressBudget budget;
    private readonly IExecutionEventStore events;
    private readonly TimeProvider clock;
    private readonly ILogger<CodexCodeWorker> logger;

    /// <summary>Creates the worker.</summary>
    public CodexCodeWorker(
        ICodexProcess codex,
        ICommandRunner commands,
        IOptions<CodexOptions> codexOptions,
        IOptions<CodeWorkOptions> options,
        IEgressBudget budget,
        IExecutionEventStore events,
        TimeProvider clock,
        ILogger<CodexCodeWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(codex);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(codexOptions);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.codex = codex;
        this.commands = commands;
        this.codexOptions = codexOptions.Value;
        this.options = options.Value;
        this.budget = budget;
        this.events = events;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public bool Enabled => this.options.Enabled;

    /// <inheritdoc />
    public async Task<CodeChange> ChangeAsync(CodeChangeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await this.RefuseIfNeededAsync(cancellationToken).ConfigureAwait(false);
        await this.EmitAsync(request.TraceId, request.Origin, ExecutionEventType.EgressRequested,
            ExecutionStatus.Running, "code change -> codex subscription", cancellationToken).ConfigureAwait(false);
        try
        {
            var change = await this.WorkAsync(request, cancellationToken).ConfigureAwait(false);
            await this.EmitAsync(request.TraceId, request.Origin, ExecutionEventType.EgressCompleted,
                ExecutionStatus.Succeeded,
                $"code change: {(change.Changed ? change.DiffStat : "no files changed")}; {change.BuildOutcome}",
                cancellationToken).ConfigureAwait(false);
            return change;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await this.EmitAsync(request.TraceId, request.Origin, ExecutionEventType.EgressFailed,
                ExecutionStatus.Failed, $"code change: {exception.GetType().Name}", cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string> ExplainAsync(CodeQuestion question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        await this.RefuseIfNeededAsync(cancellationToken).ConfigureAwait(false);
        await this.EmitAsync(question.TraceId, question.Origin, ExecutionEventType.EgressRequested,
            ExecutionStatus.Running, "code question -> codex subscription", cancellationToken).ConfigureAwait(false);
        try
        {
            var answer = await this.codex.RunAsync(
                this.codexOptions.BinaryPath,
                this.CodexArguments("read-only", this.options.Repository, ExplainPrompt(question.Question)),
                TimeSpan.FromSeconds(this.options.TimeoutSeconds), cancellationToken).ConfigureAwait(false);
            await this.EmitAsync(question.TraceId, question.Origin, ExecutionEventType.EgressCompleted,
                ExecutionStatus.Succeeded, $"code question: {answer.Length} chars answered", cancellationToken)
                .ConfigureAwait(false);
            return this.Bound(answer);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await this.EmitAsync(question.TraceId, question.Origin, ExecutionEventType.EgressFailed,
                ExecutionStatus.Failed, $"code question: {exception.GetType().Name}", cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CodeBranch>> ListChangesAsync(CancellationToken cancellationToken)
    {
        var refs = await this.GitAsync(this.options.Repository,
        [
            "for-each-ref", "--sort=-committerdate",
            "--format=%(refname:short)%09%(committerdate:iso-strict)%09%(subject)",
            "refs/heads/" + this.options.BranchPrefix,
        ], cancellationToken).ConfigureAwait(false);

        var branches = new List<CodeBranch>();
        foreach (var line in refs.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t', 3);
            if (parts.Length < 3)
            {
                continue;
            }

            var stat = await this.commands.RunAsync(this.options.Repository, "git",
                ["diff", "--shortstat", this.options.BaseBranch + "..." + parts[0]], gitTimeout, cancellationToken)
                .ConfigureAwait(false);
            branches.Add(new CodeBranch(
                parts[0], DateTimeOffset.Parse(parts[1], CultureInfo.InvariantCulture), parts[2], stat.Output.Trim()));
        }

        return branches;
    }

    private async Task<CodeChange> WorkAsync(CodeChangeRequest request, CancellationToken cancellationToken)
    {
        var name = this.clock.GetUtcNow().ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) + "-" + Slug(request.Task);
        var branch = this.options.BranchPrefix + name;
        var worktree = Path.Combine(this.options.WorktreeRoot, name);
        await this.GitAsync(this.options.Repository,
            ["worktree", "add", "-b", branch, worktree, this.options.BaseBranch], cancellationToken).ConfigureAwait(false);
        this.logger.LogInformation("Code change on {Branch} in {Worktree}", branch, worktree);

        var summary = await this.codex.RunAsync(
            this.codexOptions.BinaryPath,
            this.CodexArguments("workspace-write", worktree, ChangePrompt(request.Task)),
            TimeSpan.FromSeconds(this.options.TimeoutSeconds), cancellationToken).ConfigureAwait(false);

        var status = await this.GitAsync(worktree, ["status", "--porcelain"], cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(status))
        {
            return new CodeChange(branch, worktree, false, string.Empty, false, "not built: no files changed", this.Bound(summary));
        }

        var build = await this.BuildAsync(worktree, cancellationToken).ConfigureAwait(false);
        var committed = await this.CommitAsync(worktree, request.Task, cancellationToken).ConfigureAwait(false);
        var diffStat = await this.commands.RunAsync(worktree, "git",
            ["diff", "--shortstat", this.options.BaseBranch], gitTimeout, cancellationToken).ConfigureAwait(false);
        return new CodeChange(branch, worktree, true, diffStat.Output.Trim(), committed, build, this.Bound(summary));
    }

    private async Task<string> BuildAsync(string worktree, CancellationToken cancellationToken)
    {
        var build = await this.commands.RunAsync(worktree, "dotnet",
            ["build", this.options.Solution, "-nologo", "-v", "quiet"],
            TimeSpan.FromSeconds(this.options.BuildTimeoutSeconds), cancellationToken).ConfigureAwait(false);
        return build.ExitCode == 0
            ? "dotnet build succeeded (exit 0)"
            : $"dotnet build FAILED (exit {build.ExitCode}): {LastLines(build.Output)}";
    }

    private async Task<bool> CommitAsync(string worktree, string task, CancellationToken cancellationToken)
    {
        await this.GitAsync(worktree, ["add", "-A"], cancellationToken).ConfigureAwait(false);
        var commit = await this.commands.RunAsync(worktree, "git",
            ["commit", "-q", "-m", Subject(task)], gitTimeout, cancellationToken).ConfigureAwait(false);
        if (commit.ExitCode != 0)
        {
            this.logger.LogWarning("Commit in {Worktree} failed: {Output}", worktree, LastLines(commit.Output));
        }

        return commit.ExitCode == 0;
    }

    private async Task<string> GitAsync(string directory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await this.commands.RunAsync(directory, "git", arguments, gitTimeout, cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0
            ? result.Output
            : throw new InvalidOperationException($"git {arguments[0]} failed ({result.ExitCode}): {LastLines(result.Output)}");
    }

    private async Task RefuseIfNeededAsync(CancellationToken cancellationToken)
    {
        var refusal = !this.options.Enabled
            ? "code work is not enabled on this host (CodeWork:Enabled)"
            : !this.codexOptions.Enabled
                ? "the subscription frontier is not enabled"
                : await this.budget.FindRefusalAsync(cancellationToken).ConfigureAwait(false);
        if (refusal is null)
        {
            return;
        }

        this.logger.LogWarning("Code work refused: {Reason}", refusal);
        throw new EgressRefusedException(refusal);
    }

    private List<string> CodexArguments(string sandbox, string directory, string prompt)
    {
        var arguments = new List<string> { "exec", "--ephemeral", "--sandbox", sandbox, "--skip-git-repo-check" };
        if (this.codexOptions.Model.Length > 0)
        {
            arguments.Add("-m");
            arguments.Add(this.codexOptions.Model);
        }

        arguments.Add("--cd");
        arguments.Add(directory);
        arguments.Add(prompt);
        return arguments;
    }

    private static string ChangePrompt(string task) => $"""
        You are working in a git worktree of the Dami Core repository, on a branch made for this
        one task. Read AGENTS.md and CLAUDE.md first and follow them. Make the smallest change that
        does the task, with the tests they require, and run `dotnet build Dami.sln` from the Dami
        directory until it reports 0 warnings and 0 errors.
        Do not commit. Do not push. Do not add packages or projects. Do not touch anything outside
        this worktree. Finish with a short summary of what you changed and why, and anything you
        could not do.

        Task:
        {task}
        """;

    private static string ExplainPrompt(string question) => $"""
        You are reading the Dami Core repository. Answer the question below from the code, naming
        the files that matter. Change nothing. Keep the answer under 200 words.

        Question:
        {question}
        """;

    private string Bound(string text) =>
        text.Length <= this.options.MaxSummaryChars ? text : text[..this.options.MaxSummaryChars] + "…";

    /// <summary>The task as a branch-safe word list: lower case, hyphens, bounded.</summary>
    private static string Slug(string task)
    {
        var slug = new StringBuilder(SLUG_LENGTH);
        var pendingHyphen = false;
        foreach (var character in task.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                if (pendingHyphen && slug.Length > 0)
                {
                    slug.Append('-');
                }

                slug.Append(character);
                pendingHyphen = false;
            }
            else
            {
                pendingHyphen = true;
            }

            if (slug.Length >= SLUG_LENGTH)
            {
                break;
            }
        }

        return slug.Length == 0 ? "task" : slug.ToString().TrimEnd('-');
    }

    private static string Subject(string task)
    {
        var line = task.ReplaceLineEndings("\n").Split('\n', 2)[0].Trim();
        return line.Length <= SUBJECT_LENGTH ? line : line[..SUBJECT_LENGTH].TrimEnd() + "…";
    }

    private static string LastLines(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var tail = lines.Length <= 3 ? lines : lines[^3..];
        var flat = string.Join(" | ", tail).Trim();
        return flat.Length <= 300 ? flat : flat[..300] + "…";
    }

    private Task<long> EmitAsync(
        Guid traceId, ExecutionOrigin origin, ExecutionEventType type, ExecutionStatus status,
        string label, CancellationToken cancellationToken) =>
        this.events.AppendAsync(new ExecutionEvent(
            Guid.NewGuid(), traceId, Guid.NewGuid(), null, origin, ACTOR, type, status,
            this.clock.GetUtcNow(), label), cancellationToken);
}
