using System.Text.Json;

namespace Dami.Gateway.Cli;

/// <summary>Replays a trace through the runtime API, child spans indented (§8.1).</summary>
public sealed class TraceCommands
{
    private readonly DamiApiClient api;

    /// <summary>Creates the commands.</summary>
    public TraceCommands(DamiApiClient api)
    {
        ArgumentNullException.ThrowIfNull(api);
        this.api = api;
    }

    /// <summary>Prints one trace, oldest first. Accepts full ids or 8-char short ids.</summary>
    public Task<int> ReplayAsync(string traceId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(traceId);
        return ApiCall.RunAsync(async () =>
        {
            using var reply = await this.api.GetAsync($"/traces/{traceId}", cancellationToken)
                .ConfigureAwait(false);
            if (reply is null)
            {
                await Console.Error.WriteLineAsync(
                    $"'{traceId}' is not a trace id or unique short id").ConfigureAwait(false);
                return 1;
            }

            var items = reply.RootElement.EnumerateArray().ToList();
            var depths = DepthsBySpan(items);
            foreach (var item in items)
            {
                Print(item, depths.GetValueOrDefault(item.GetProperty("spanId").GetGuid()));
            }

            return items.Count > 0 ? 0 : 1;
        });
    }

    private static Dictionary<Guid, int> DepthsBySpan(List<JsonElement> items)
    {
        var parents = new Dictionary<Guid, Guid?>();
        foreach (var item in items)
        {
            var parent = item.GetProperty("parentSpanId");
            parents.TryAdd(
                item.GetProperty("spanId").GetGuid(),
                parent.ValueKind == JsonValueKind.Null ? null : parent.GetGuid());
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

    private static void Print(JsonElement item, int depth)
    {
        var status = item.GetProperty("status").GetString();
        var marker = status switch
        {
            "Succeeded" => "done ",
            "Failed" => "FAIL ",
            "Cancelled" => "stop ",
            "Running" => "run  ",
            _ => "     ",
        };
        var occurredAt = item.GetProperty("occurredAt").GetDateTimeOffset();
        var indent = new string(' ', depth * 2);
        Console.WriteLine(
            $"{occurredAt:HH:mm:ss} {marker}{indent}{item.GetProperty("type").GetString(),-20} "
            + $"{item.GetProperty("actorId").GetString(),-15} {item.GetProperty("label").GetString()}");
    }
}
