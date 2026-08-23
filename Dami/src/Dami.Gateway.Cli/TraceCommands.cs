using Dami.Contracts.Events;

namespace Dami.Gateway.Cli;

/// <summary>Replays a trace from the canonical event store.</summary>
/// <remarks>
/// The CLI rendering of charter §8.1's trace tree, minimal form. Everything shown is a
/// persisted event — the display invents nothing, which is the trust boundary in §7.4.
/// </remarks>
public sealed class TraceCommands
{
    private readonly IExecutionEventStore eventStore;

    /// <summary>Creates the commands.</summary>
    public TraceCommands(IExecutionEventStore eventStore)
    {
        ArgumentNullException.ThrowIfNull(eventStore);
        this.eventStore = eventStore;
    }

    /// <summary>Prints one trace, oldest first.</summary>
    public async Task<int> ReplayAsync(string traceId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(traceId);

        if (!Guid.TryParse(traceId, out var parsed))
        {
            await Console.Error.WriteLineAsync($"'{traceId}' is not a trace id").ConfigureAwait(false);
            return 1;
        }

        var any = false;
        await foreach (var item in this.eventStore.ReplayAsync(parsed, cancellationToken)
            .ConfigureAwait(false))
        {
            any = true;
            Print(item);
        }

        if (any)
        {
            return 0;
        }

        await Console.Error.WriteLineAsync($"no events for trace {parsed}").ConfigureAwait(false);
        return 1;
    }

    private static void Print(ExecutionEvent item)
    {
        var marker = item.Status switch
        {
            ExecutionStatus.Succeeded => "done ",
            ExecutionStatus.Failed => "FAIL ",
            ExecutionStatus.Cancelled => "stop ",
            ExecutionStatus.Running => "run  ",
            _ => "     ",
        };
        Console.WriteLine(
            $"{item.OccurredAt:HH:mm:ss} {marker}{item.Type,-20} {item.ActorId,-15} {item.Label}");
    }
}
