namespace Dami.Gateway.Cli;

/// <summary>`dami domain [name]` and `dami domain-reject` — the shared domain facts (K4).</summary>
public sealed class DomainCommands
{
    private readonly DamiApiClient api;

    /// <summary>Creates the commands.</summary>
    public DomainCommands(DamiApiClient api)
    {
        ArgumentNullException.ThrowIfNull(api);
        this.api = api;
    }

    /// <summary>Lists domains, or one domain's facts newest first.</summary>
    public Task<int> ShowAsync(string? domain, CancellationToken cancellationToken)
    {
        return ApiCall.RunAsync(async () =>
        {
            if (domain is null)
            {
                using var domains = await this.api.GetAsync("/domains", cancellationToken).ConfigureAwait(false);
                var any = false;
                foreach (var item in domains!.RootElement.EnumerateArray())
                {
                    any = true;
                    Console.WriteLine($"{item.GetProperty("domain").GetString(),-10} {item.GetProperty("facts").GetInt32(),5} facts");
                }

                Console.WriteLine(any ? "\ndami domain <name> for the timeline" : "no domain facts yet - collectors run nightly");
                return 0;
            }

            using var reply = await this.api.GetAsync($"/domains/{Uri.EscapeDataString(domain)}", cancellationToken)
                .ConfigureAwait(false);
            foreach (var fact in reply!.RootElement.EnumerateArray())
            {
                Console.WriteLine(
                    $"{fact.GetProperty("factId").GetGuid().ToString("N")[..8]}  {fact.GetProperty("asOf").GetString()}  "
                    + $"[{fact.GetProperty("category").GetString()}]  {fact.GetProperty("description").GetString()}");
            }

            Console.WriteLine("\nwrong? dami domain-reject <id8> \"reason\"");
            return 0;
        });
    }

    /// <summary>Rejects a wrong fact permanently.</summary>
    public Task<int> RejectAsync(string idPrefix, string? reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(idPrefix);
        return ApiCall.RunAsync(async () =>
        {
            using var reply = await this.api.PostAsync(
                $"/domains/facts/{idPrefix}/reject", new { reason }, cancellationToken).ConfigureAwait(false);
            if (reply is null)
            {
                await Console.Error.WriteLineAsync($"no domain fact matches '{idPrefix}'").ConfigureAwait(false);
                return 1;
            }

            Console.WriteLine($"rejected: {reply.RootElement.GetProperty("rejected").GetString()}");
            return 0;
        });
    }
}
