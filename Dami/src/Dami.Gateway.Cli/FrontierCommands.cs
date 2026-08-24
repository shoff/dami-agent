namespace Dami.Gateway.Cli;

/// <summary>A bare question to the frontier (ADR-0011), via the runtime API.</summary>
public sealed class FrontierCommands
{
    private readonly DamiApiClient api;

    /// <summary>Creates the commands.</summary>
    public FrontierCommands(DamiApiClient api)
    {
        ArgumentNullException.ThrowIfNull(api);
        this.api = api;
    }

    /// <summary>Sends one bare question and prints the answer.</summary>
    public Task<int> AskAsync(string question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        return ApiCall.RunAsync(async () =>
        {
            Console.WriteLine("asking the frontier (subscription, no API billing)...");
            using var reply = await this.api.PostAsync("/frontier", new { question }, cancellationToken)
                .ConfigureAwait(false);
            var root = reply!.RootElement;
            Console.WriteLine();
            Console.WriteLine(root.GetProperty("answer").GetString());
            Console.WriteLine();
            var trace = root.GetProperty("traceId").GetGuid().ToString("N")[..8];
            Console.WriteLine($"[frontier via codex subscription · no memories sent · trace {trace}]");
            return 0;
        });
    }
}
