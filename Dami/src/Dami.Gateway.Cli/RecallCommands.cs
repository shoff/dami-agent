namespace Dami.Gateway.Cli;

/// <summary>Semantic recall over the corpus, from the runtime API.</summary>
public sealed class RecallCommands
{
    private readonly DamiApiClient api;

    /// <summary>Creates the commands.</summary>
    public RecallCommands(DamiApiClient api)
    {
        ArgumentNullException.ThrowIfNull(api);
        this.api = api;
    }

    /// <summary>Searches the corpus and prints the best matches, best first.</summary>
    public Task<int> SearchAsync(string query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return ApiCall.RunAsync(async () =>
        {
            using var reply = await this.api.GetAsync(
                $"/recall?q={Uri.EscapeDataString(query)}", cancellationToken).ConfigureAwait(false);
            var any = false;
            foreach (var item in reply!.RootElement.EnumerateArray())
            {
                any = true;
                Console.WriteLine(
                    $"{item.GetProperty("occurredAt").GetDateTimeOffset():yyyy-MM-dd}  "
                    + $"[{item.GetProperty("source").GetString()}]  "
                    + item.GetProperty("body").GetString());
            }

            if (!any)
            {
                Console.WriteLine("the corpus has no indexed observations yet - the embedder runs nightly");
            }

            return 0;
        });
    }
}
