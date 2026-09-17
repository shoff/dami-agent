using Dami.Contracts.Events;
using Dami.Contracts.Research;

namespace Dami.Core.Frontier;

/// <summary>Owns research recording for one authoritative runtime instance.</summary>
public sealed partial class ResearchJournal
{
    private readonly IResearchRunStore store;
    private readonly TimeProvider clock;
    private readonly Guid ownerId = Guid.NewGuid();

    /// <summary>Creates a journal over durable artifact storage.</summary>
    public ResearchJournal(IResearchRunStore store, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        this.store = store;
        this.clock = clock;
    }

    internal async Task<ResearchProgress> BeginAsync(IReadOnlyList<Uri> seeds, string question, Guid traceId,
        ExecutionOrigin origin, bool automaticSources, CancellationToken cancellationToken)
    {
        var run = new ResearchRun(Guid.NewGuid(), traceId, question, seeds[0], this.clock.GetUtcNow())
        {
            StartingUrls = seeds,
            AutomaticSources = automaticSources,
            OwnerId = this.ownerId,
            Origin = origin,
        };
        await this.store.SaveAsync(run, cancellationToken).ConfigureAwait(false);
        return new ResearchProgress(run, this.store, this.clock);
    }

    /// <summary>Reads an artifact, marking an unfinished earlier-runtime traversal interrupted.</summary>
    public async Task<ResearchRun?> GetAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await this.store.GetAsync(runId, cancellationToken).ConfigureAwait(false);
        if (run is { Status: ResearchRunStatus.Running } && run.OwnerId != this.ownerId)
        {
            run = run with
            {
                Revision = run.Revision + 1,
                UpdatedAt = this.clock.GetUtcNow(),
                Status = ResearchRunStatus.Interrupted,
                CurrentUrl = null,
                Error = "The previous runtime stopped before recording an outcome. Saved sources remain available.",
            };
            await this.store.SaveAsync(run, cancellationToken).ConfigureAwait(false);
        }

        return run;
    }

    /// <summary>Reads lightweight history and reconciles interrupted earlier-runtime entries.</summary>
    public async Task<IReadOnlyList<ResearchRunSummary>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        var history = await this.store.ListAsync(limit, cancellationToken).ConfigureAwait(false);
        var result = new List<ResearchRunSummary>(history.Count);
        foreach (var summary in history)
        {
            var run = summary.Status == ResearchRunStatus.Running
                ? await this.GetAsync(summary.RunId, cancellationToken).ConfigureAwait(false)
                : null;
            result.Add(run?.Summarize() ?? summary);
        }

        return result;
    }
}
