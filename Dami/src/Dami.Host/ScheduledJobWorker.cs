using Dami.Core.Scheduling;

namespace Dami.Host;

internal sealed class ScheduledJobWorker : BackgroundService
{
    private static readonly TimeSpan interval = TimeSpan.FromSeconds(30);
    private readonly ScheduledJobDispatcher dispatcher;

    public ScheduledJobWorker(ScheduledJobDispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);
        do
        {
            await this.dispatcher.RunDueAsync(stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
