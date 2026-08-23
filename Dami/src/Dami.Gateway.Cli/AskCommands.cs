using System.Text;
using Dami.Contracts.Memory;
using Dami.Contracts.Models;

namespace Dami.Gateway.Cli;

/// <summary>Question answering over the corpus — retrieval plus local synthesis.</summary>
/// <remarks>
/// The full local pipeline: embed → ANN → rerank → the sidecar answers FROM the
/// retrieved observations only, citing them by number. The question, the memories, and
/// the answer never leave the host. Grounding rule in the prompt: if the observations
/// do not answer it, say so — an assistant that invents memories about Steve is worse
/// than one that admits a gap.
/// </remarks>
public sealed class AskCommands
{
    private const int CANDIDATES = 24;
    private const int CONTEXT = 8;

    private readonly IObservationEmbeddingStore embeddingStore;
    private readonly IEmbeddingClient embeddingClient;
    private readonly IRerankClient rerankClient;
    private readonly IChatClient chatClient;
    private readonly TimeProvider clock;

    /// <summary>Creates the commands.</summary>
    public AskCommands(
        IObservationEmbeddingStore embeddingStore,
        IEmbeddingClient embeddingClient,
        IRerankClient rerankClient,
        IChatClient chatClient,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(embeddingStore);
        ArgumentNullException.ThrowIfNull(embeddingClient);
        ArgumentNullException.ThrowIfNull(rerankClient);
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(clock);

        this.embeddingStore = embeddingStore;
        this.embeddingClient = embeddingClient;
        this.rerankClient = rerankClient;
        this.chatClient = chatClient;
        this.clock = clock;
    }

    /// <summary>Answers a question from the corpus, with citations.</summary>
    public async Task<int> AskAsync(string question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);

        var context = await this.RetrieveAsync(question, cancellationToken).ConfigureAwait(false);
        if (context.Count == 0)
        {
            Console.WriteLine("the corpus has nothing indexed yet");
            return 0;
        }

        Console.WriteLine("thinking (local model - this takes seconds, not milliseconds)...");
        var answer = await this.chatClient
            .CompleteAsync(BuildPrompt(question, context, this.clock.GetUtcNow()), cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine(answer.Trim());
        Console.WriteLine();
        Console.WriteLine("sources:");
        for (var index = 0; index < context.Count; index++)
        {
            var observation = context[index];
            Console.WriteLine(
                $"  [{index + 1}] {observation.OccurredAt:yyyy-MM-dd} {observation.Source}: "
                + Shorten(observation.Body));
        }

        return 0;
    }

    private async Task<List<Observation>> RetrieveAsync(string question, CancellationToken cancellationToken)
    {
        var queryVector = (await this.embeddingClient
            .EmbedAsync([question], cancellationToken).ConfigureAwait(false))[0];

        var candidates = new List<Observation>();
        await foreach (var (observation, _) in this.embeddingStore
            .NearestAsync(queryVector, this.embeddingClient.ModelId, CANDIDATES, cancellationToken)
            .ConfigureAwait(false))
        {
            candidates.Add(observation);
        }

        return candidates.Count == 0
            ? candidates
            : await this.KeepBestAsync(question, candidates, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<Observation>> KeepBestAsync(
        string question,
        List<Observation> candidates,
        CancellationToken cancellationToken)
    {
        var bodies = new List<string>(candidates.Count);
        foreach (var candidate in candidates)
        {
            bodies.Add(candidate.Body);
        }

        var order = await this.rerankClient
            .RankAsync(question, bodies, cancellationToken).ConfigureAwait(false);

        var context = new List<Observation>(CONTEXT);
        foreach (var index in order)
        {
            context.Add(candidates[index]);
            if (context.Count >= CONTEXT)
            {
                break;
            }
        }

        return context;
    }

    private static string BuildPrompt(string question, List<Observation> context, DateTimeOffset today)
    {
        var prompt = new StringBuilder();
        prompt.Append("Today is ").Append(today.ToString("yyyy-MM-dd"))
            .AppendLine(". Observations carry their own dates; old ones are history, not the present.");
        prompt.AppendLine(
            "Answer the question using ONLY the numbered observations below, citing them like [2].");
        prompt.AppendLine(
            "If they do not contain the answer, say plainly that the memories do not cover it.");
        prompt.AppendLine("Be concise - a few sentences.");
        prompt.AppendLine();

        for (var index = 0; index < context.Count; index++)
        {
            prompt.Append(index + 1).Append(". [").Append(context[index].OccurredAt.ToString("yyyy-MM-dd"))
                .Append("] ").AppendLine(context[index].Body);
        }

        prompt.AppendLine();
        prompt.Append("Question: ").AppendLine(question);
        return prompt.ToString();
    }

    private static string Shorten(string body)
    {
        var flat = body.ReplaceLineEndings(" ");
        return flat.Length <= 100 ? flat : flat[..100] + "…";
    }
}
