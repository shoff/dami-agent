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

    /// <summary>Prints one trace, oldest first, child spans indented (charter §8.1).</summary>
    public async Task<int> ReplayAsync(string traceId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(traceId);

        var parsed = await this.ResolveAsync(traceId, cancellationToken).ConfigureAwait(false);
        if (parsed is null)
        {
            await Console.Error.WriteLineAsync(
                $"'{traceId}' is not a trace id or unique short id").ConfigureAwait(false);
            return 1;
        }

        var items = new List<ExecutionEvent>();
        await foreach (var item in this.eventStore.ReplayAsync(parsed.Value, cancellationToken)
            .ConfigureAwait(false))
        {
            items.Add(item);
        }

        if (items.Count == 0)
        {
            await Console.Error.WriteLineAsync($"no events for trace {parsed}").ConfigureAwait(false);
            return 1;
        }

        var depths = DepthsBySpan(items);
        foreach (var item in items)
        {
            Print(item, depths.GetValueOrDefault(item.SpanId));
        }

        return 0;
    }

    private async Task<Guid?> ResolveAsync(string traceId, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(traceId, out var parsed))
        {
            return parsed;
        }

        if (traceId.Length >= 6 && traceId.All(Uri.IsHexDigit))
        {
            return await this.eventStore.FindTraceByPrefixAsync(traceId, cancellationToken)
                .ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>Span depth from parent links — the §8.1 tree, computed not asserted.</summary>
    private static Dictionary<Guid, int> DepthsBySpan(List<ExecutionEvent> items)
    {
        var parents = new Dictionary<Guid, Guid?>();
        foreach (var item in items)
        {
            parents.TryAdd(item.SpanId, item.ParentSpanId);
        }

        var depths = new Dictionary<Guid, int>();
        foreach (var spanId in parents.Keys)
        {
            var depth = 0;
            var current = parents[spanId];
            while (current is not null && depth < 16 && parents.TryGetValue(current.Value, out var next))
            {
                depth++;
                current = next;
            }

            depths[spanId] = depth;
        }

        return depths;
    }

    private static void Print(ExecutionEvent item, int depth)
    {
        var marker = item.Status switch
        {
            ExecutionStatus.Succeeded => "done ",
            ExecutionStatus.Failed => "FAIL ",
            ExecutionStatus.Cancelled => "stop ",
            ExecutionStatus.Running => "run  ",
            _ => "     ",
        };
        var indent = new string(' ', depth * 2);
        Console.WriteLine(
            $"{item.OccurredAt:HH:mm:ss} {marker}{indent}{item.Type,-20} {item.ActorId,-15} {item.Label}");
    }
}
