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
                Console.WriteLine(
                    $"{item.GetProperty("eventDate").GetString()}  "
                    + $"[{item.GetProperty("category").GetString()}]  "
                    + item.GetProperty("description").GetString());
            }

            if (!any)
            {
                Console.WriteLine("no health events extracted yet - the collector runs nightly");
            }

            return 0;
        });
    }
}
