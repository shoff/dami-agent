using System.Text.Json;

namespace Dami.Gateway.Cli;

/// <summary>The inbox: list, read, and react — through the runtime API (D-005).</summary>
public sealed class InboxCommands
{
    private readonly DamiApiClient api;

    /// <summary>Creates the commands.</summary>
    public InboxCommands(DamiApiClient api)
    {
        ArgumentNullException.ThrowIfNull(api);
        this.api = api;
    }

    /// <summary>Lists pending surfacings.</summary>
    public Task<int> ListPendingAsync(CancellationToken cancellationToken)
    {
        return ApiCall.RunAsync(async () =>
        {
            using var reply = await this.api.GetAsync("/surfacings", cancellationToken).ConfigureAwait(false);
            var any = PrintList(reply!);
            if (!any)
            {
                Console.WriteLine("nothing pending - the muse is quiet");
            }

            return 0;
        });
    }

    /// <summary>Lists recent surfacings in every status.</summary>
    public Task<int> ListRecentAsync(CancellationToken cancellationToken)
    {
        return ApiCall.RunAsync(async () =>
        {
            using var reply = await this.api.GetAsync("/surfacings/recent", cancellationToken)
                .ConfigureAwait(false);
            PrintList(reply!);
            return 0;
        });
    }

    /// <summary>Shows one surfacing in full; the runtime marks it delivered.</summary>
    public Task<int> ReadAsync(string idPrefix, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(idPrefix);
        return ApiCall.RunAsync(async () =>
        {
            using var reply = await this.api.GetAsync($"/surfacings/{idPrefix}", cancellationToken)
                .ConfigureAwait(false);
            if (reply is null)
            {
                await Console.Error.WriteLineAsync($"no surfacing matches '{idPrefix}'").ConfigureAwait(false);
                return 1;
            }

            var item = reply.RootElement;
            Console.WriteLine(item.GetProperty("title").GetString());
            Console.WriteLine($"  {item.GetProperty("body").GetString()}");
            Console.WriteLine(
                $"  from {item.GetProperty("serviceName").GetString()}, "
                + $"confidence {item.GetProperty("confidence").GetDouble():0.00}");
            Console.WriteLine($"  react with: dami good|bad|meh {Short(item)} [note]");
            return 0;
        });
    }

    /// <summary>Records a reaction. The runtime also writes it into the corpus.</summary>
    public Task<int> FeedbackAsync(
        string idPrefix,
        string verdict,
        string? note,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(idPrefix);
        return ApiCall.RunAsync(async () =>
        {
            using var reply = await this.api.PostAsync(
                $"/surfacings/{idPrefix}/feedback", new { verdict, note }, cancellationToken)
                .ConfigureAwait(false);
            if (reply is null)
            {
                await Console.Error.WriteLineAsync($"no surfacing matches '{idPrefix}'").ConfigureAwait(false);
                return 1;
            }

            var feedback = reply.RootElement.GetProperty("feedback").GetString();
            Console.WriteLine($"recorded '{feedback}' - this trains the taste model");
            return 0;
        });
    }

    private static bool PrintList(JsonDocument reply)
    {
        var any = false;
        foreach (var item in reply.RootElement.EnumerateArray())
        {
            any = true;
            Console.WriteLine(
                $"{Short(item)}  {item.GetProperty("confidence").GetDouble():0.00}  "
                + item.GetProperty("title").GetString());
        }

        return any;
    }

    private static string Short(JsonElement item)
    {
        return item.GetProperty("surfacingId").GetGuid().ToString("N")[..8];
    }
}
