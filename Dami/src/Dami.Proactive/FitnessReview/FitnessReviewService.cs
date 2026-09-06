using Dami.Contracts.Domains;
using Dami.Contracts.Memory;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;

namespace Dami.Proactive.FitnessReview;

/// <summary>Once a week, the log says what a coach would: the week, the records, the plateaus, what was skipped.</summary>
/// <remarks>
/// The charter's sentence in its most literal form — something Steve did not ask for,
/// from data only this host holds, with the numbers beside every claim. It surfaces; the
/// gateway raises it on his next message, and the inbox keeps it.
/// </remarks>
public sealed class FitnessReviewService : IProactiveService
{
    private const int MAX_LINES = 6;

    private readonly IFitnessStore store;
    private readonly TimeProvider clock;
    private readonly ILogger<FitnessReviewService> logger;

    /// <summary>Creates the review.</summary>
    public FitnessReviewService(IFitnessStore store, TimeProvider clock, ILogger<FitnessReviewService> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.store = store;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ServiceName => "fitness-review";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.Weekly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(ProactiveContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var snapshot = await this.store.SnapshotAsync(cancellationToken).ConfigureAwait(false);
        var now = this.clock.GetUtcNow();
        var insights = FitnessInsights.Analyze(snapshot, now);
        if (insights.Count == 0)
        {
            return ProactiveResult.quiet;
        }

        var body = string.Join("\n", insights
            .OrderBy(insight => insight.Kind == FitnessInsightKind.WeeklySummary ? 0 : 1)
            .Take(MAX_LINES)
            .Select(insight => insight.Text));
        this.logger.LogInformation("Fitness review: {Count} insight(s)", insights.Count);
        return new ProactiveResult(
            Array.Empty<Conclusion>(),
            [new Surfacing(Guid.NewGuid(), this.ServiceName, "Your week in the gym", body, 0.7, now)],
            ProactiveStatus.Completed,
            $"{insights.Count} insight(s)");
    }
}
