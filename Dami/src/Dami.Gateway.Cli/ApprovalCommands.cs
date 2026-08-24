using System.Text.Json;

namespace Dami.Gateway.Cli;

/// <summary>The approval queue — resolution and execution both happen in the runtime.</summary>
public sealed class ApprovalCommands
{
    private readonly DamiApiClient api;

    /// <summary>Creates the commands.</summary>
    public ApprovalCommands(DamiApiClient api)
    {
        ArgumentNullException.ThrowIfNull(api);
        this.api = api;
    }

    /// <summary>Lists pending approvals.</summary>
    public Task<int> ListAsync(CancellationToken cancellationToken)
    {
        return ApiCall.RunAsync(async () =>
        {
            using var reply = await this.api.GetAsync("/approvals", cancellationToken).ConfigureAwait(false);
            var any = false;
            foreach (var item in reply!.RootElement.EnumerateArray())
            {
                any = true;
                Console.WriteLine(
                    $"{item.GetProperty("approvalId").GetGuid().ToString("N")[..8]}  "
                    + $"[{item.GetProperty("scope").GetString()}] "
                    + $"{item.GetProperty("requestedBy").GetString()}: "
                    + item.GetProperty("action").GetString());
            }

            if (!any)
            {
                Console.WriteLine("nothing awaits approval");
            }

            return 0;
        });
    }

    /// <summary>Approves a request; the runtime executes what the approval gates.</summary>
    public Task<int> ApproveAsync(string idPrefix, CancellationToken cancellationToken)
    {
        return this.ResolveAsync(idPrefix, approve: true, note: null, cancellationToken);
    }

    /// <summary>Denies a request. It never runs.</summary>
    public Task<int> DenyAsync(string idPrefix, string? note, CancellationToken cancellationToken)
    {
        return this.ResolveAsync(idPrefix, approve: false, note, cancellationToken);
    }

    private Task<int> ResolveAsync(
        string idPrefix,
        bool approve,
        string? note,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(idPrefix);
        return ApiCall.RunAsync(async () =>
        {
            using var reply = await this.api.PostAsync(
                $"/approvals/{idPrefix}/resolve", new { approve, note }, cancellationToken)
                .ConfigureAwait(false);
            if (reply is null)
            {
                await Console.Error.WriteLineAsync(
                    $"no pending approval matches '{idPrefix}'").ConfigureAwait(false);
                return 1;
            }

            var root = reply.RootElement;
            var verb = approve ? "approved" : "denied";
            Console.WriteLine($"{verb}: {root.GetProperty("action").GetString()}");
            if (root.TryGetProperty("execution", out var execution)
                && execution.ValueKind == JsonValueKind.String)
            {
                Console.WriteLine();
                Console.WriteLine(execution.GetString());
            }

            return 0;
        });
    }
}
