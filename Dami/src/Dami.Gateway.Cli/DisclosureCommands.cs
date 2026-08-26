namespace Dami.Gateway.Cli;

/// <summary>`dami disclosures` and `dami disclose-correct` — review and correct the gate (G9a).</summary>
public sealed class DisclosureCommands
{
    private const int WIDTH = 70;

    private readonly DamiApiClient api;

    /// <summary>Creates the commands.</summary>
    public DisclosureCommands(DamiApiClient api)
    {
        ArgumentNullException.ThrowIfNull(api);
        this.api = api;
    }

    /// <summary>Prints recent gate decisions, newest first, with corrections where made.</summary>
    public Task<int> ListAsync(CancellationToken cancellationToken)
    {
        return ApiCall.RunAsync(async () =>
        {
            using var reply = await this.api.GetAsync("/disclosures?limit=50", cancellationToken).ConfigureAwait(false);
            var any = false;
            foreach (var item in reply!.RootElement.EnumerateArray())
            {
                any = true;
                var id8 = item.GetProperty("decisionId").GetGuid().ToString("N")[..8];
                var decided = item.GetProperty("disclosure").GetString() ?? string.Empty;
                var correction = item.GetProperty("correction");
                var corrected = correction.ValueKind == System.Text.Json.JsonValueKind.Object
                    ? $"  → corrected to {correction.GetProperty("corrected").GetString()}"
                    : string.Empty;
                Console.WriteLine($"{id8}  {decided,-8}  {Shorten(item.GetProperty("original").GetString() ?? string.Empty)}{corrected}");
            }

            Console.WriteLine(any
                ? "\nwrong? dami disclose-correct <id8> pass|disguise|withhold \"why\""
                : "no gate decisions recorded yet - they appear after an augmented frontier turn");
            return 0;
        });
    }

    /// <summary>Records what the gate should have decided. The note is the lesson.</summary>
    public Task<int> CorrectAsync(string idPrefix, string disclosure, string? note, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(idPrefix);
        ArgumentNullException.ThrowIfNull(disclosure);
        return ApiCall.RunAsync(async () =>
        {
            var outcome = await this.api.MutateAsync(
                HttpMethod.Post, $"/disclosures/{idPrefix}/correct",
                new { disclosure, note, correctedBy = BoardActor.FromEnvironment().ActorId },
                cancellationToken).ConfigureAwait(false);
            switch (outcome)
            {
                case true:
                    Console.WriteLine($"corrected to {disclosure}: the gate will see this as an example");
                    return 0;
                case false:
                    await Console.Error.WriteLineAsync("that decision was already corrected").ConfigureAwait(false);
                    return 1;
                default:
                    await Console.Error.WriteLineAsync($"no recent gate decision matches '{idPrefix}'").ConfigureAwait(false);
                    return 1;
            }
        });
    }

    private static string Shorten(string text)
    {
        var flat = text.ReplaceLineEndings(" ");
        return flat.Length <= WIDTH ? flat : flat[..(WIDTH - 1)] + "…";
    }
}
