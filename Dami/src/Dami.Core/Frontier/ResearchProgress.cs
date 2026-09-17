using Dami.Contracts.Research;

namespace Dami.Core.Frontier;

/// <summary>A single traversal's monotonically versioned snapshots.</summary>
internal sealed class ResearchProgress(ResearchRun run, IResearchRunStore store, TimeProvider clock)
{
    private ResearchRun current = run;

    internal IReadOnlyList<ResearchIssue> Issues => this.current.Issues;

    internal Task ReadingAsync(Uri url, CancellationToken token) => this.SaveAsync(this.current with
    {
        CurrentUrl = url,
        PagesAttempted = this.current.PagesAttempted + 1,
    }, token);

    internal Task SkippedAsync(Uri url, string reason, CancellationToken token) => this.SaveAsync(this.current with
    {
        CurrentUrl = null,
        Issues = [.. this.current.Issues, new ResearchIssue(url, reason)],
    }, token);

    internal Task FoundAsync(ResearchFinding finding, int references, CancellationToken token) => this.SaveAsync(this.current with
    {
        CurrentUrl = null,
        Findings = [.. this.current.Findings, finding],
        ReferencesConsidered = references,
    }, token);

    internal Task CompletedAsync(DeepResearchResult result, CancellationToken token) => this.SaveAsync(this.current with
    {
        Status = ResearchRunStatus.Completed,
        CurrentUrl = null,
        PageLimitReached = result.PageLimitReached,
    }, token);

    internal async Task StoppedAsync(Exception exception)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await this.SaveAsync(this.current with
        {
            Status = exception is OperationCanceledException ? ResearchRunStatus.Cancelled : ResearchRunStatus.Failed,
            CurrentUrl = null,
            Error = exception.Message,
        }, cleanup.Token).ConfigureAwait(false);
    }

    private async Task SaveAsync(ResearchRun snapshot, CancellationToken token)
    {
        this.current = snapshot with { Revision = this.current.Revision + 1, UpdatedAt = clock.GetUtcNow() };
        await store.SaveAsync(this.current, token).ConfigureAwait(false);
    }
}
