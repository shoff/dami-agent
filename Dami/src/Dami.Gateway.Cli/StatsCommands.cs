namespace Dami.Gateway.Cli;

/// <summary>One screen of what the system has been doing, from the runtime API.</summary>
public sealed class StatsCommands
{
    private readonly DamiApiClient api;

    /// <summary>Creates the commands.</summary>
    public StatsCommands(DamiApiClient api)
    {
        ArgumentNullException.ThrowIfNull(api);
        this.api = api;
    }

    /// <summary>Prints the system's vital signs.</summary>
    public Task<int> ShowAsync(CancellationToken cancellationToken)
    {
        return ApiCall.RunAsync(async () =>
        {
            using var reply = await this.api.GetAsync("/stats", cancellationToken).ConfigureAwait(false);
            foreach (var section in reply!.RootElement.EnumerateObject())
            {
                Console.WriteLine(section.Name);
                var any = false;
                foreach (var line in section.Value.EnumerateArray())
                {
                    any = true;
                    Console.WriteLine($"  {line.GetString()}");
                }

                if (!any)
                {
                    Console.WriteLine("  (none)");
                }

                Console.WriteLine();
            }

            return 0;
        });
    }
}
