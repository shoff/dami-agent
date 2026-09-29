using Dami.Contracts.Scheduling;

namespace Dami.Core.Scheduling;

/// <summary>Where a Prompt job's answer goes when it names a channel (migration 039).</summary>
public interface IScheduledPromptDelivery
{
    /// <summary>Whether this delivery serves the job's <c>Delivery</c> key.</summary>
    bool Handles(string? delivery);

    /// <summary>
    /// Runs <paramref name="prompt"/> for <paramref name="job"/> in its channel and returns what
    /// was said. When <paramref name="quiet"/>, nothing is shown until the whole answer is in,
    /// and an answer of <see cref="JobPrompt.NOTHING_NEW"/> is not shown at all.
    /// </summary>
    Task<string> DeliverAsync(ScheduledJob job, string prompt, bool quiet, CancellationToken cancellationToken);
}
