using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Proactive;

/// <summary>The H8 tuner: a pure, bounded function of recorded reactions.</summary>
/// <remarks>
/// effective = base + (negativeShare − positiveShare) · Gain, clamped to
/// [base − MaxLower, base + MaxRaise]. Stateless by design — the threshold is
/// recomputed from the reaction record every pass, so there is no accumulator a
/// feedback loop could ratchet. Fewer than <see cref="ThresholdTuningOptions.MinimumReactions"/>
/// reactions, or none at all, means the base threshold, untouched: no evidence, no opinion.
/// </remarks>
public sealed class ReactionThresholdTuner : ISurfacingThresholdTuner
{
    private readonly ISurfacingQueue surfacingQueue;
    private readonly ThresholdTuningOptions tuningOptions;
    private readonly ILogger<ReactionThresholdTuner> logger;

    /// <summary>Creates the tuner.</summary>
    public ReactionThresholdTuner(
        ISurfacingQueue surfacingQueue,
        IOptions<ThresholdTuningOptions> tuningOptions,
        ILogger<ReactionThresholdTuner> logger)
    {
        ArgumentNullException.ThrowIfNull(surfacingQueue);
        ArgumentNullException.ThrowIfNull(tuningOptions);
        ArgumentNullException.ThrowIfNull(logger);

        this.surfacingQueue = surfacingQueue;
        this.tuningOptions = tuningOptions.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<double> EffectiveThresholdAsync(
        string serviceName,
        double baseThreshold,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceName);

        var (positive, negative, total) = await this.CountAsync(serviceName, cancellationToken)
            .ConfigureAwait(false);
        if (total < this.tuningOptions.MinimumReactions)
        {
            return baseThreshold;
        }

        var lean = ((double)(negative - positive)) / total;
        var effective = Math.Clamp(
            baseThreshold + (lean * this.tuningOptions.Gain),
            baseThreshold - this.tuningOptions.MaxLower,
            baseThreshold + this.tuningOptions.MaxRaise);

        this.logger.LogInformation(
            "Threshold for {Service}: {Effective:F3} (base {Base:F2}, {Positive}+/{Negative}- of {Total})",
            serviceName, effective, baseThreshold, positive, negative, total);
        return effective;
    }

    private async Task<(int Positive, int Negative, int Total)> CountAsync(
        string serviceName,
        CancellationToken cancellationToken)
    {
        var positive = 0;
        var negative = 0;
        var total = 0;
        await foreach (var reaction in this.surfacingQueue
            .ReactionsForServiceAsync(serviceName, this.tuningOptions.Window, cancellationToken)
            .ConfigureAwait(false))
        {
            total++;
            positive += reaction.IsPositive ? 1 : 0;
            negative += reaction.IsNegative ? 1 : 0;
        }

        return (positive, negative, total);
    }
}
