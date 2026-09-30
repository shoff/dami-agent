using Dami.Contracts.Finance;
using Dami.Contracts.Memory;

namespace Dami.Core.Reliability;

/// <summary>What Dami did since a moment, as counts — no content (docs/agent-landscape-2026-09.md C11).</summary>
/// <remarks>
/// One user's agent wrote a weekly post about what it had done; the useful part was knowing
/// it had been doing anything. Counts only, so the line is operational and may join the
/// Sunday notice without the disclosure gate.
/// </remarks>
public sealed class WeeklyActivity
{
    private readonly IObservationCorpus corpus;
    private readonly IExpenseLedger expenses;
    private readonly TimeProvider clock;

    /// <summary>Creates the counter.</summary>
    public WeeklyActivity(IObservationCorpus corpus, IExpenseLedger expenses, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(expenses);
        ArgumentNullException.ThrowIfNull(clock);
        this.corpus = corpus;
        this.expenses = expenses;
        this.clock = clock;
    }

    /// <summary>One sentence of counts since <paramref name="since"/>.</summary>
    public async Task<string> LineAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        var now = this.clock.GetUtcNow();
        var sources = new Dictionary<string, int>(StringComparer.Ordinal);
        await foreach (var observation in this.corpus.BetweenAsync(since, now, cancellationToken).ConfigureAwait(false))
        {
            sources[observation.Source] = sources.GetValueOrDefault(observation.Source) + 1;
        }

        var receipts = await this.expenses.BetweenAsync(
            DateOnly.FromDateTime(since.UtcDateTime), DateOnly.FromDateTime(now.UtcDateTime), cancellationToken).ConfigureAwait(false);
        return $"This week I answered {sources.GetValueOrDefault("chat")} message(s), took {sources.GetValueOrDefault("lesson")} lesson(s), "
            + $"kept {sources.GetValueOrDefault("diary")} diary entry(ies) and logged {receipts.Count} receipt(s).";
    }
}
