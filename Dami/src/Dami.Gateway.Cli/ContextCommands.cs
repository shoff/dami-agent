using System.Text.Json;

namespace Dami.Gateway.Cli;

/// <summary>Shows exactly what would enter the prompt, from the runtime API.</summary>
public sealed class ContextCommands
{
    private readonly DamiApiClient api;

    /// <summary>Creates the commands.</summary>
    public ContextCommands(DamiApiClient api)
    {
        ArgumentNullException.ThrowIfNull(api);
        this.api = api;
    }

    /// <summary>Prints the assembled context and its token cost.</summary>
    public Task<int> ShowAsync(string request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ApiCall.RunAsync(async () =>
        {
            using var reply = await this.api.GetAsync(
                $"/context?q={Uri.EscapeDataString(request)}", cancellationToken).ConfigureAwait(false);
            var root = reply!.RootElement;
            var beliefs = root.GetProperty("beliefs");
            var memories = root.GetProperty("memories");
            Console.WriteLine(
                $"~{root.GetProperty("estimatedTokens").GetInt32()} tokens  "
                + $"({beliefs.GetArrayLength()} beliefs, {memories.GetArrayLength()} memories)");
            Console.WriteLine();
            foreach (var item in beliefs.EnumerateArray())
            {
                Console.WriteLine($"belief  {FormatAsOf(item)}  {item.GetProperty("content").GetString()}");
            }

            foreach (var item in memories.EnumerateArray())
            {
                Console.WriteLine($"memory  {FormatAsOf(item)}  {Shorten(item.GetProperty("content").GetString()!)}");
            }

            return 0;
        });
    }

    private static string FormatAsOf(JsonElement item)
    {
        var asOf = item.GetProperty("asOf").GetDateTimeOffset();
        return asOf.Year < 1971 ? "undated   " : asOf.ToString("yyyy-MM-dd");
    }

    private static string Shorten(string content)
    {
        var flat = content.ReplaceLineEndings(" ");
        return flat.Length <= 130 ? flat : flat[..130] + "…";
    }
}
