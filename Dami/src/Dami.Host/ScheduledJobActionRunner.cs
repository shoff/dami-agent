using System.Diagnostics;
using Dami.Contracts.Proactive;
using Dami.Contracts.Scheduling;
using Dami.Core.Frontier;
using Dami.Core.Scheduling;

namespace Dami.Host;

/// <summary>Runs one scheduled job: a Prompt through the frontier, a Command as a process.</summary>
/// <remarks>
/// A Prompt job that names a channel is delivered there as a turn with the tool bundle;
/// one that does not is answered by the augmented frontier turn and surfaced to the
/// inbox. Neither path touches the local model (ADR-0028 applies to jobs too).
/// </remarks>
public sealed class ScheduledJobActionRunner : IScheduledJobActionRunner
{
    private const string SERVICE = "scheduled-job";

    private readonly IReadOnlyList<IScheduledPromptDelivery> deliveries;
    private readonly IAugmentedTurn augmented;
    private readonly ISurfacingQueue surfacings;
    private readonly IScheduledJobRunLog runs;
    private readonly TimeProvider clock;

    /// <summary>Creates the runner.</summary>
    public ScheduledJobActionRunner(
        IEnumerable<IScheduledPromptDelivery> deliveries,
        IAugmentedTurn augmented,
        ISurfacingQueue surfacings,
        IScheduledJobRunLog runs,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(deliveries);
        ArgumentNullException.ThrowIfNull(augmented);
        ArgumentNullException.ThrowIfNull(surfacings);
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(clock);
        this.deliveries = deliveries.ToList();
        this.augmented = augmented;
        this.surfacings = surfacings;
        this.runs = runs;
        this.clock = clock;
    }

    /// <inheritdoc />
    public Task RunAsync(ScheduledJob job, CancellationToken cancellationToken) =>
        job.Kind switch
        {
            ScheduledJobKind.Prompt => this.RunPromptAsync(job, cancellationToken),
            ScheduledJobKind.Command => RunCommandAsync(job, cancellationToken),
            _ => throw new InvalidOperationException($"Unknown scheduled job kind {job.Kind}."),
        };

    /// <summary>
    /// The job sees what it said on its last runs; a change-only job with history may answer
    /// "NOTHING NEW", which is shown nowhere and recorded as not delivered (C1, C2).
    /// </summary>
    private async Task RunPromptAsync(ScheduledJob job, CancellationToken cancellationToken)
    {
        var recent = await this.runs.RecentAsync(job.JobId, JobPrompt.REMEMBERED_RUNS, cancellationToken).ConfigureAwait(false);
        var prompt = JobPrompt.Compose(job, recent);
        var quiet = job.OnlyWhenNew && recent.Count > 0;
        var delivery = this.deliveries.FirstOrDefault(candidate => candidate.Handles(job.Delivery));
        var said = delivery is not null
            ? await delivery.DeliverAsync(job, prompt, quiet, cancellationToken).ConfigureAwait(false)
            : (await this.augmented.RunAsync(prompt, cancellationToken).ConfigureAwait(false)).Answer;
        var silent = quiet && JobPrompt.IsNothingNew(said);
        if (delivery is null && !silent)
        {
            await this.surfacings.EnqueueAsync(
                new Surfacing(Guid.NewGuid(), SERVICE, job.Name, said, 0.9, this.clock.GetUtcNow()),
                cancellationToken).ConfigureAwait(false);
        }

        await this.runs.RecordAsync(
            new ScheduledJobRun(Guid.NewGuid(), job.JobId, this.clock.GetUtcNow(), said, !silent),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task RunCommandAsync(
        ScheduledJob job,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(job.Payload)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in job.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"Could not start {job.Payload}.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        _ = await output.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{job.Payload} exited {process.ExitCode}: {(await error.ConfigureAwait(false)).Trim()}");
        }
    }
}
