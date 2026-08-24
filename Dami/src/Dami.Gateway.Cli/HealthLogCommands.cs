namespace Dami.Gateway.Cli;

/// <summary>`dami health-log` — the structured health timeline (K2), via the runtime API.</summary>
public sealed class HealthLogCommands
{
    private readonly DamiApiClient api;

    /// <summary>Creates the commands.</summary>
    public HealthLogCommands(DamiApiClient api)
    {
        ArgumentNullException.ThrowIfNull(api);
        this.api = api;
    }

    /// <summary>Prints the health timeline, newest first.</summary>
    public Task<int> ShowAsync(CancellationToken cancellationToken)
    {
        return ApiCall.RunAsync(async () =>
        {
            using var reply = await this.api.GetAsync("/health-log", cancellationToken)
                .ConfigureAwait(false);
            var any = false;
            foreach (var item in reply!.RootElement.EnumerateArray())
            {
                any = true;
                var date = item.GetProperty("eventDate").GetString();
                var id8 = item.GetProperty("healthEventId").GetGuid().ToString("N")[..8];
                Console.WriteLine(
                    $"{id8}  "
                    + $"{(date is not null && date.StartsWith("1970", StringComparison.Ordinal) ? "undated   " : date)}  "
                    + $"[{item.GetProperty("category").GetString()}]  "
                    + item.GetProperty("description").GetString());
            }

            if (!any)
            {
                Console.WriteLine("no health events extracted yet - the collector runs nightly");
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("wrong? dami health-reject <id8> \"reason\"");
            }

            return 0;
        });
    }

    /// <summary>Rejects a wrong fact permanently — a later pass will not resurrect it.</summary>
    public Task<int> RejectAsync(string idPrefix, string? reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(idPrefix);
        return ApiCall.RunAsync(async () =>
        {
            using var reply = await this.api.PostAsync(
                $"/health-log/{idPrefix}/reject", new { reason }, cancellationToken)
                .ConfigureAwait(false);
            if (reply is null)
            {
                await Console.Error.WriteLineAsync($"no health fact matches '{idPrefix}'")
                    .ConfigureAwait(false);
                return 1;
            }

            Console.WriteLine($"rejected: {reply.RootElement.GetProperty("rejected").GetString()}");
            Console.WriteLine("  it will not come back on the next collector pass");
            return 0;
        });
    }
}
