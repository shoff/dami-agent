using System.Diagnostics;
using Dami.Contracts.Scheduling;
using Dami.Core.Scheduling;
using Dami.Core.Sessions;
using Dami.Core.Turns;

namespace Dami.Host;

internal sealed class ScheduledJobActionRunner : IScheduledJobActionRunner
{
    private readonly ITracedTurnRunner turnRunner;

    public ScheduledJobActionRunner(ITracedTurnRunner turnRunner)
    {
        this.turnRunner = turnRunner;
    }

    public Task RunAsync(ScheduledJob job, CancellationToken cancellationToken) =>
        job.Kind switch
        {
            ScheduledJobKind.Prompt => this.RunPromptAsync(job, cancellationToken),
            ScheduledJobKind.Command => RunCommandAsync(job, cancellationToken),
            _ => throw new InvalidOperationException($"Unknown scheduled job kind {job.Kind}."),
        };

    private async Task RunPromptAsync(ScheduledJob job, CancellationToken cancellationToken)
    {
        _ = await this.turnRunner.RunTracedAsync(
            Guid.NewGuid(), job.Payload, ConversationWindow.Empty, cancellationToken)
            .ConfigureAwait(false);
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
