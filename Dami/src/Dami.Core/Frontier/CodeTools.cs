using System.Globalization;
using System.Text;
using System.Text.Json;
using Dami.Contracts.Code;
using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Microsoft.Extensions.Logging;

namespace Dami.Core.Frontier;

/// <summary>Lets the frontier change, explain, and list work on the code this host runs (ADR-0036).</summary>
public interface IFrontierCode
{
    /// <summary>The tools to offer this turn. Empty when code work is switched off.</summary>
    IReadOnlyList<FrontierTool> Tools { get; }

    /// <summary>Carries out a task on a fresh branch and describes the result for the model.</summary>
    Task<FrontierToolResult> ChangeAsync(Guid traceId, string task, CancellationToken cancellationToken);

    /// <summary>Answers a question about the code without changing it.</summary>
    Task<FrontierToolResult> ExplainAsync(Guid traceId, string question, CancellationToken cancellationToken);

    /// <summary>Lists the branches earlier tasks produced.</summary>
    Task<FrontierToolResult> ListAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The fourth door, in the model's vocabulary. A task goes to <see cref="ICodeWorker"/>
/// and what comes back is a branch name, a diffstat, this host's own build verdict and the
/// coding agent's summary — never "done and live". The model is told, in the result it
/// reads, that nothing is merged, so it cannot honestly tell Steve otherwise.
/// </summary>
public sealed class CodeTools : IFrontierCode
{
    /// <summary>The tool that changes code on a new branch.</summary>
    public const string CHANGE_CODE = "change_code";

    /// <summary>The tool that explains code without changing it.</summary>
    public const string EXPLAIN_CODE = "explain_code";

    /// <summary>The tool that lists the branches earlier changes produced.</summary>
    public const string LIST_CODE_CHANGES = "list_code_changes";

    private const string NOT_MERGED =
        "It is not merged, not pushed, not deployed: Steve reviews the branch and merges it himself. "
        + "Tell him the branch name and that it is waiting for his review.";

    private static readonly IReadOnlyList<FrontierTool> none = [];

    private static readonly IReadOnlyList<FrontierTool> tools =
    [
        new(
            CHANGE_CODE,
            "Change your own source code (the Dami Core repository) or create new code in it: fix a "
            + "bug, add a feature, adjust behaviour Steve asks for. A coding agent does the work on a "
            + "new git branch and this host builds it; nothing is merged or deployed. Takes minutes. "
            + "Describe the task fully in one go, including what files or behaviour it concerns.",
            Schema("task", "What to change and why, as you would brief an engineer. Two to five sentences.")),
        new(
            EXPLAIN_CODE,
            "Answer a question about how your own code works by reading the repository. Changes "
            + "nothing. Use it before change_code when you are unsure where something lives.",
            Schema("question", "The question, in one or two sentences.")),
        new(
            LIST_CODE_CHANGES,
            "List the branches your earlier change_code calls produced, newest first, with what "
            + "each changed. Use it when Steve asks what you have changed or what is waiting for review.",
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new { },
                additionalProperties = false,
            })),
    ];

    private readonly ICodeWorker worker;
    private readonly ILogger<CodeTools> logger;

    /// <summary>Creates the tools.</summary>
    public CodeTools(ICodeWorker worker, ILogger<CodeTools> logger)
    {
        ArgumentNullException.ThrowIfNull(worker);
        ArgumentNullException.ThrowIfNull(logger);
        this.worker = worker;
        this.logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<FrontierTool> Tools => this.worker.Enabled ? tools : none;

    /// <inheritdoc />
    public async Task<FrontierToolResult> ChangeAsync(Guid traceId, string task, CancellationToken cancellationToken)
    {
        try
        {
            var change = await this.worker
                .ChangeAsync(new CodeChangeRequest(task, traceId, ExecutionOrigin.UserTurn), cancellationToken)
                .ConfigureAwait(false);
            return FrontierToolResult.Ok(Describe(change));
        }
        catch (EgressRefusedException refusal)
        {
            this.logger.LogWarning("Turn {Trace}: code change refused: {Reason}", traceId, refusal.Message);
            return FrontierToolResult.Failed($"{CHANGE_CODE} was refused: {refusal.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<FrontierToolResult> ExplainAsync(Guid traceId, string question, CancellationToken cancellationToken)
    {
        try
        {
            var answer = await this.worker
                .ExplainAsync(new CodeQuestion(question, traceId, ExecutionOrigin.UserTurn), cancellationToken)
                .ConfigureAwait(false);
            return FrontierToolResult.Ok(answer);
        }
        catch (EgressRefusedException refusal)
        {
            this.logger.LogWarning("Turn {Trace}: code question refused: {Reason}", traceId, refusal.Message);
            return FrontierToolResult.Failed($"{EXPLAIN_CODE} was refused: {refusal.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<FrontierToolResult> ListAsync(CancellationToken cancellationToken)
    {
        var branches = await this.worker.ListChangesAsync(cancellationToken).ConfigureAwait(false);
        if (branches.Count == 0)
        {
            return FrontierToolResult.Ok("No code changes on record: no branch from an earlier change_code exists.");
        }

        var text = new StringBuilder("Branches, newest first (branch | last commit | subject | change against main):\n");
        foreach (var branch in branches)
        {
            text.Append(branch.Name).Append(" | ")
                .Append(branch.CommittedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(branch.Subject).Append(" | ").Append(branch.DiffStat).Append('\n');
        }

        return FrontierToolResult.Ok(text.ToString().TrimEnd());
    }

    private static string Describe(CodeChange change) =>
        change.Changed
            ? $"Done on branch {change.Branch} (worktree {change.WorktreePath}): {change.DiffStat}. "
              + $"{change.BuildOutcome}. {NOT_MERGED}\nThe coding agent's summary: {change.Summary}"
            : $"The coding agent did not change any file; branch {change.Branch} is empty. "
              + $"Its summary: {change.Summary}";

    private static JsonElement Schema(string argument, string description) =>
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new Dictionary<string, object> { [argument] = new { type = "string", description } },
            required = new[] { argument },
            additionalProperties = false,
        });
}
