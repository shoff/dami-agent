using Dami.Contracts.Scheduling;
using Dami.Core.Scheduling;

namespace Dami.Host.Discord;

/// <summary>A scheduled job drafted on Discord comes back as a Discord turn (ADR-0030).</summary>
/// <remarks>
/// The same answerer as a live message, tool bundle included, so "a picture every morning"
/// is answered exactly as "a picture now" would be — into the channel that asked.
/// </remarks>
public sealed class DiscordScheduledDelivery : IScheduledPromptDelivery
{
    private const string PREFIX = "discord:";

    private readonly DiscordAnswerer answerer;

    /// <summary>Creates the delivery.</summary>
    public DiscordScheduledDelivery(DiscordAnswerer answerer)
    {
        ArgumentNullException.ThrowIfNull(answerer);
        this.answerer = answerer;
    }

    /// <inheritdoc />
    public bool Handles(string? delivery) =>
        delivery is not null && delivery.StartsWith(PREFIX, StringComparison.Ordinal) && delivery.Length > PREFIX.Length;

    /// <inheritdoc />
    /// <remarks>
    /// A run whose answer never reached Discord is a failed run, whatever the conversation
    /// was told. The dispatcher records the exception's message on the job, so the reason
    /// is the same one the channel saw.
    /// </remarks>
    public async Task DeliverAsync(ScheduledJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (!this.Handles(job.Delivery))
        {
            throw new ArgumentException($"job {job.JobId} is not a Discord delivery", nameof(job));
        }

        var outcome = await this.answerer.AnswerAsync(
                job.Delivery![PREFIX.Length..], $"[scheduled job '{job.Name}'] {job.Payload}", [], cancellationToken)
            .ConfigureAwait(false);
        if (!outcome.IsAnswered)
        {
            throw new InvalidOperationException(outcome.Failure);
        }
    }
}
