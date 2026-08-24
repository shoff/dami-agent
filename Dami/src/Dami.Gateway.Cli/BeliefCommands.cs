using System.Text.Json;

namespace Dami.Gateway.Cli;

/// <summary>The ledger, readable and correctable — through the runtime API (F-09/F-10).</summary>
public sealed class BeliefCommands
{
    private readonly DamiApiClient api;

    /// <summary>Creates the commands.</summary>
    public BeliefCommands(DamiApiClient api)
    {
        ArgumentNullException.ThrowIfNull(api);
        this.api = api;
    }

    /// <summary>Prints the currently believed set, or the set as of a date.</summary>
    public Task<int> ListAsync(string? asOf, CancellationToken cancellationToken)
    {
        return ApiCall.RunAsync(async () =>
        {
            var query = asOf is null ? "/beliefs" : $"/beliefs?asOf={Uri.EscapeDataString(asOf)}";
            using var reply = await this.api.GetAsync(query, cancellationToken).ConfigureAwait(false);
            var any = false;
            foreach (var item in reply!.RootElement.EnumerateArray())
            {
                any = true;
                Print(item);
            }

            if (!any)
            {
                Console.WriteLine(
                    "the ledger holds no active conclusions" + (asOf is null ? "" : $" as of {asOf}"));
            }

            return 0;
        });
    }

    /// <summary>Prints what changed between two moments — the drift instrument.</summary>
    public Task<int> DiffAsync(string from, string? to, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(from);
        return ApiCall.RunAsync(async () =>
        {
            var query = $"/beliefs/diff?from={Uri.EscapeDataString(from)}"
                + (to is null ? "" : $"&to={Uri.EscapeDataString(to)}");
            using var reply = await this.api.GetAsync(query, cancellationToken).ConfigureAwait(false);
            var added = reply!.RootElement.GetProperty("added").EnumerateArray().ToList();
            var removed = reply.RootElement.GetProperty("removed").EnumerateArray().ToList();
            foreach (var item in added)
            {
                Console.WriteLine($"+ {item.GetProperty("statement").GetString()}");
            }

            foreach (var item in removed)
            {
                var reason = item.GetProperty("retractionReason").GetString() ?? "superseded";
                Console.WriteLine($"- {item.GetProperty("statement").GetString()}  [{reason}]");
            }

            if (added.Count == 0 && removed.Count == 0)
            {
                Console.WriteLine("no drift: the believed set is unchanged");
            }

            return 0;
        });
    }

    /// <summary>Retracts a belief — the correction taking effect.</summary>
    public Task<int> RetractAsync(string idPrefix, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(idPrefix);
        ArgumentNullException.ThrowIfNull(reason);
        return ApiCall.RunAsync(async () =>
        {
            using var reply = await this.api.PostAsync(
                $"/beliefs/{idPrefix}/retract", new { reason }, cancellationToken).ConfigureAwait(false);
            if (reply is null)
            {
                await Console.Error.WriteLineAsync(
                    $"no active conclusion matches '{idPrefix}'").ConfigureAwait(false);
                return 1;
            }

            Console.WriteLine($"retracted: {reply.RootElement.GetProperty("retracted").GetString()}");
            Console.WriteLine($"  reason: {reason}");
            return 0;
        });
    }

    /// <summary>Replaces a belief with a corrected one — supersession, not deletion.</summary>
    public Task<int> CorrectAsync(
        string idPrefix,
        string correctedStatement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(idPrefix);
        ArgumentNullException.ThrowIfNull(correctedStatement);
        return ApiCall.RunAsync(async () =>
        {
            using var reply = await this.api.PostAsync(
                $"/beliefs/{idPrefix}/correct", new { statement = correctedStatement }, cancellationToken)
                .ConfigureAwait(false);
            if (reply is null)
            {
                await Console.Error.WriteLineAsync(
                    $"no active conclusion matches '{idPrefix}'").ConfigureAwait(false);
                return 1;
            }

            Console.WriteLine($"was:    {reply.RootElement.GetProperty("was").GetString()}");
            Console.WriteLine($"now:    {reply.RootElement.GetProperty("now").GetString()}");
            Console.WriteLine("        (confidence 1.00 - a direct correction outranks any inference)");
            return 0;
        });
    }

    /// <summary>Records an observation from the command line into the corpus.</summary>
    public Task<int> NoteAsync(string body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        return ApiCall.RunAsync(async () =>
        {
            using var reply = await this.api.PostAsync("/observations", new { body }, cancellationToken)
                .ConfigureAwait(false);
            var id = reply!.RootElement.GetProperty("observationId").GetGuid();
            Console.WriteLine($"noted ({id.ToString("N")[..8]})");
            return 0;
        });
    }

    private static void Print(JsonElement item)
    {
        var supporting = item.GetProperty("supportingObservations").GetArrayLength();
        var provenance = supporting > 0 ? $"{supporting} obs" : "no provenance";
        Console.WriteLine(
            $"{item.GetProperty("conclusionId").GetGuid().ToString("N")[..8]}  "
            + $"{item.GetProperty("confidence").GetDouble():0.00}  "
            + $"[{item.GetProperty("source").GetString()}, {provenance}]  "
            + item.GetProperty("statement").GetString());
    }
}
