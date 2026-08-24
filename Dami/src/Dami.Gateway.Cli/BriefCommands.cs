namespace Dami.Gateway.Cli;

/// <summary>`dami brief` — the C4 consent flow, via the runtime API (ADR-0013).</summary>
public sealed class BriefCommands
{
    private readonly DamiApiClient api;

    /// <summary>Creates the commands.</summary>
    public BriefCommands(DamiApiClient api)
    {
        ArgumentNullException.ThrowIfNull(api);
        this.api = api;
    }

    /// <summary>Drafts the brief and files the approval. Prints the exact bytes for review.</summary>
    public Task<int> DraftAsync(string question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        return ApiCall.RunAsync(async () =>
        {
            Console.WriteLine("assembling context and drafting a redacted brief (local model)...");
            using var reply = await this.api.PostAsync("/briefs", new { question }, cancellationToken)
                .ConfigureAwait(false);
            var root = reply!.RootElement;
            var approvalId = root.GetProperty("approvalId").GetGuid().ToString("N")[..8];

            Console.WriteLine();
            Console.WriteLine("---- exact bytes that would egress ----");
            Console.WriteLine(root.GetProperty("brief").GetString());
            Console.WriteLine("---------------------------------------");
            Console.WriteLine(
                $"context drawn from {root.GetProperty("contextItems").GetInt32()} item(s); "
                + $"sha256 {root.GetProperty("sha256").GetString()![..12]}…");
            Console.WriteLine($"review the text above, then: dami approve {approvalId}");
            Console.WriteLine($"or: dami deny {approvalId} \"reason\"");
            return 0;
        });
    }
}
