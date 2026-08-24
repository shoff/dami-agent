using Dami.Contracts.Models;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Proactive.CodeAudit;

/// <summary>Weekly local review of the repo's recent changes (D-016).</summary>
/// <remarks>
/// Reads the recent patch, asks the loopback model for the single most consequential
/// defect, and surfaces at most one finding with a suggested fix. It writes nothing,
/// stages nothing, commits nothing — a proposal in the queue is its entire authority.
/// Egress-free by construction: the diff goes only to the loopback sidecar.
/// </remarks>
public sealed class CodebaseAuditService : IProactiveService
{
    private const string NO_FINDING = "NONE";

    private static readonly string instructions =
        $"""
        You are reviewing a patch from a C# codebase. Identify the SINGLE most
        consequential defect, risk, or broken invariant in the changes — a real
        problem, not style. If nothing rises to that bar, reply exactly {NO_FINDING}.
        Otherwise reply with: one line naming the problem and file, then a short
        suggested fix (a patch sketch is welcome). Be terse.
        """;

    private readonly IGitLog gitLog;
    private readonly IChatClient chatClient;
    private readonly CodebaseAuditOptions auditOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<CodebaseAuditService> logger;

    /// <summary>Creates the service.</summary>
    public CodebaseAuditService(
        IGitLog gitLog,
        IChatClient chatClient,
        IOptions<CodebaseAuditOptions> auditOptions,
        TimeProvider clock,
        ILogger<CodebaseAuditService> logger)
    {
        ArgumentNullException.ThrowIfNull(gitLog);
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(auditOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.gitLog = gitLog;
        this.chatClient = chatClient;
        this.auditOptions = auditOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ServiceName => "codebase-audit";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.Weekly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(
        ProactiveContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var patch = await this.gitLog.RecentPatchAsync(
            this.auditOptions.RepoPath, TimeSpan.FromHours(this.auditOptions.WindowHours),
            cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(patch))
        {
            return ProactiveResult.quiet;
        }

        if (patch.Length > this.auditOptions.MaxPatchChars)
        {
            patch = patch[..this.auditOptions.MaxPatchChars];
        }

        var review = await this.chatClient.CompleteAsync(
            $"{instructions}\n\nPatch:\n{patch}", cancellationToken).ConfigureAwait(false);
        return this.BuildResult(review.Trim());
    }

    private ProactiveResult BuildResult(string review)
    {
        if (review.Length == 0
            || review.StartsWith(NO_FINDING, StringComparison.OrdinalIgnoreCase))
        {
            this.logger.LogInformation("Codebase audit: no finding this pass");
            return ProactiveResult.quiet;
        }

        var title = review.Split('\n', 2)[0];
        var surfacing = new Surfacing(
            Guid.NewGuid(), this.ServiceName,
            title.Length > 120 ? title[..120] : title,
            review, confidence: 0.6, this.clock.GetUtcNow());
        return new ProactiveResult([], [surfacing], ProactiveStatus.Completed);
    }
}
