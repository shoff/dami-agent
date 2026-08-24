namespace Dami.Gateway.Cli;

/// <summary>Answers a question from the corpus with citations, via the runtime API.</summary>
public sealed class AskCommands
{
    private readonly DamiApiClient api;

    /// <summary>Creates the commands.</summary>
    public AskCommands(DamiApiClient api)
    {
        ArgumentNullException.ThrowIfNull(api);
        this.api = api;
    }

    /// <summary>Asks, prints the answer and its numbered sources.</summary>
    public Task<int> AskAsync(string question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        return ApiCall.RunAsync(async () =>
        {
            Console.WriteLine("thinking (local model - this takes seconds, not milliseconds)...");
            using var reply = await this.api.PostAsync("/ask", new { question }, cancellationToken)
                .ConfigureAwait(false);
            var root = reply!.RootElement;
            var answer = root.GetProperty("answer");
            if (answer.ValueKind == System.Text.Json.JsonValueKind.Null)
            {
                Console.WriteLine("the corpus has nothing indexed yet");
                return 0;
            }

            Console.WriteLine();
            Console.WriteLine(answer.GetString());
            Console.WriteLine();
            Console.WriteLine("sources:");
            var index = 0;
            foreach (var source in root.GetProperty("sources").EnumerateArray())
            {
                var body = source.GetProperty("body").GetString()!.ReplaceLineEndings(" ");
                Console.WriteLine(
                    $"  [{++index}] {source.GetProperty("occurredAt").GetDateTimeOffset():yyyy-MM-dd} "
                    + $"{source.GetProperty("source").GetString()}: "
                    + (body.Length <= 100 ? body : body[..100] + "…"));
            }

            return 0;
        });
    }
}
