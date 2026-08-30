using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Proactive.Hygiene;

/// <summary>Notices when work is stranded on this disk.</summary>
/// <remarks>
/// Steve's stated safety net is that nothing on this machine matters provided the
/// repository is committed and pushed. On 2026-08-29 that assumption had been false for
/// four days: <c>main</c> was 52 commits ahead of origin, the last fetch was 25 August,
/// and nothing in the system could have said so. A safety net nobody checks is a belief,
/// not a net.
///
/// It also compares the database's migration ledger against what git tracks, because
/// earlier the same day a migration was applied to <c>dami-data</c> and its file was never
/// committed — the schema and the repository disagreeing, invisible until someone happened
/// to run <c>git status</c>.
///
/// Read-only throughout. It never commits, never pushes, never stages: a nightly job with
/// write authority over a working copy is one bad heuristic away from pushing half a
/// thought. It surfaces, and a hand does the rest.
/// </remarks>
public sealed class RepoHygieneService : IProactiveService
{
    private readonly IRepoState repo;
    private readonly ISchemaLedger ledger;
    private readonly RepoHygieneOptions hygieneOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<RepoHygieneService> logger;

    /// <summary>Creates the service.</summary>
    public RepoHygieneService(
        IRepoState repo,
        ISchemaLedger ledger,
        IOptions<RepoHygieneOptions> hygieneOptions,
        TimeProvider clock,
        ILogger<RepoHygieneService> logger)
    {
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(hygieneOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.repo = repo;
        this.ledger = ledger;
        this.hygieneOptions = hygieneOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ServiceName => "repo-hygiene";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.Nightly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(
        ProactiveContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var state = await this.repo
            .ReadAsync(this.hygieneOptions.RepositoryPath, cancellationToken).ConfigureAwait(false);
        if (!state.IsRepository)
        {
            this.logger.LogWarning(
                "Repo hygiene: {Path} is not a readable git repository", this.hygieneOptions.RepositoryPath);
            return ProactiveResult.quiet;
        }

        var applied = await this.ledger.AppliedAsync(cancellationToken).ConfigureAwait(false);
        var findings = RepoHygiene.Assess(state, applied, this.clock.GetUtcNow(), this.hygieneOptions);
        this.logger.LogInformation("Repo hygiene: {Count} finding(s)", findings.Count);

        return findings.Count == 0
            ? ProactiveResult.quiet
            : new ProactiveResult([], [this.Surface(findings)], ProactiveStatus.Completed);
    }

    /// <remarks>
    /// One surfacing for the lot, not one each. Four separate rows saying the same thing —
    /// "your work is not where you think it is" — would train the eye to skip them, which
    /// is how an inbox stops being read.
    /// </remarks>
    private Surfacing Surface(IReadOnlyList<string> findings)
    {
        var headline = findings.Count == 1
            ? findings[0]
            : $"{findings.Count} things are adrift in the working copy";

        return new Surfacing(
            Guid.NewGuid(),
            this.ServiceName,
            headline,
            string.Join("\n", findings.Select(finding => $"· {finding}")),
            1.0,
            this.clock.GetUtcNow());
    }
}
