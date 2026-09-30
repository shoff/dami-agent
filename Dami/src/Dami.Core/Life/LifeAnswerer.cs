using System.Globalization;
using System.Text;
using Dami.Contracts.Models;

namespace Dami.Core.Life;

/// <summary>One dated record from Steve's own life that may answer a question.</summary>
public sealed record LifeRecord(DateTimeOffset At, string Kind, string Text);

/// <summary>An answer and the numbered records it was drawn from; a null answer when nothing matched.</summary>
public sealed record LifeAnswer(string? Answer, IReadOnlyList<LifeRecord> Sources);

/// <summary>One place Steve's records live: the corpus, receipts, mail, calendar, meals, INR.</summary>
public interface ILifeSource
{
    /// <summary>Records that may bear on <paramref name="question"/>; empty when none.</summary>
    Task<IReadOnlyList<LifeRecord>> FindAsync(string question, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>
/// "Ask your life anything" (docs/agent-landscape-2026-09.md §5, #3): Steve's question answered
/// by the local model from his own records, numbered and cited, nothing leaving the host.
/// </summary>
/// <remarks>
/// The answer cannot be better than the records, so the model is told to answer only from them
/// and to say so when they do not cover it; every source is returned for him to check. A
/// source that fails costs its records, not the answer.
/// </remarks>
public sealed class LifeAnswerer
{
    private const int RECORDS = 20;

    private readonly IReadOnlyList<ILifeSource> sources;
    private readonly IChatClient local;
    private readonly TimeProvider clock;

    /// <summary>Creates the answerer.</summary>
    public LifeAnswerer(IEnumerable<ILifeSource> sources, IChatClient local, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(clock);
        this.sources = [.. sources];
        this.local = local;
        this.clock = clock;
    }

    /// <summary>Answers from the records; no model call when no record matched.</summary>
    public async Task<LifeAnswer> AnswerAsync(string question, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        var now = this.clock.GetUtcNow();
        var records = new List<LifeRecord>();
        foreach (var source in this.sources)
        {
            try
            {
                records.AddRange(await source.FindAsync(question, now, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One unreachable source costs its records, not the answer.
            }
        }

        records = [.. records.Take(RECORDS)];
        if (records.Count == 0)
        {
            return new LifeAnswer(null, records);
        }

        var answer = await this.local.CompleteAsync(Prompt(question, records, now), cancellationToken).ConfigureAwait(false);
        return new LifeAnswer(answer.Trim(), records);
    }

    private static string Prompt(string question, List<LifeRecord> records, DateTimeOffset today)
    {
        var prompt = new StringBuilder()
            .Append("Today is ").Append(today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .AppendLine(". These are Steve's own records, each with its date; old ones are history.")
            .AppendLine("Answer his question using ONLY the numbered records below, citing them like [2].")
            .AppendLine("If they do not answer it, say plainly that his records do not cover it. A few sentences at most.")
            .AppendLine();
        for (var index = 0; index < records.Count; index++)
        {
            prompt.Append(CultureInfo.InvariantCulture,
                $"{index + 1}. [{records[index].At.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} {records[index].Kind}] {records[index].Text}\n");
        }

        return prompt.AppendLine().Append("Question: ").AppendLine(question).ToString();
    }
}
