using Dami.Contracts.Scheduling;

namespace Dami.Core.Scheduling;

/// <summary>Where a Prompt job's answer goes when it names a channel (migration 039).</summary>
public interface IScheduledPromptDelivery
{
    /// <summary>Whether this delivery serves the job's <c>Delivery</c> key.</summary>
    bool Handles(string? delivery);

    /// <summary>Runs the job's request as a turn in that channel and delivers the answer there.</summary>
    Task DeliverAsync(ScheduledJob job, CancellationToken cancellationToken);
}
