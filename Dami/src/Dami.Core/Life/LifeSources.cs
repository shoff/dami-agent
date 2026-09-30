using System.Globalization;
using System.Text.RegularExpressions;
using Dami.Contracts.Anticoag;
using Dami.Contracts.Calendar;
using Dami.Contracts.Memory;
using Dami.Contracts.Domains;
using Dami.Contracts.Finance;
using Dami.Contracts.Models;
using Dami.Contracts.Nutrition;

namespace Dami.Core.Life;

/// <summary>The words of a question worth matching records on.</summary>
public static class LifeWords
{
    private static readonly HashSet<string> questionWords = new(StringComparer.Ordinal)
    {
        "when", "what", "where", "which", "whom", "whose", "last", "that", "this", "with", "from", "about", "said", "says",
        "they", "their", "them", "there", "then", "than", "been", "were", "your", "much", "many", "lately", "time", "times",
        "ever", "into", "over", "some", "just", "also", "would", "could", "should", "will", "know", "tell", "show", "have",
        "does", "done", "doing", "recently", "anything", "something", "remember",
    };

    /// <summary>Lower-case words of four letters or more that are not question words.</summary>
    public static IReadOnlyList<string> Of(string question)
    {
        ArgumentNullException.ThrowIfNull(question);
        return [.. Regex.Matches(question.ToLowerInvariant(), @"[a-z0-9']{4,}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            .Select(match => match.Value.Trim('\''))
            .Where(word => word.Length >= 4 && !questionWords.Contains(word))
            .Distinct()];
    }

    /// <summary>Whether <paramref name="text"/> contains any of <paramref name="words"/>.</summary>
    public static bool Matches(string text, IReadOnlyList<string> words) =>
        words.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
}

/// <summary>The corpus: memories, conversations, diary entries, lessons — by meaning, then reranked.</summary>
public sealed class CorpusLifeSource(IObservationEmbeddingStore store, IEmbeddingClient embedder, IRerankClient reranker) : ILifeSource
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<LifeRecord>> FindAsync(string question, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var vector = (await embedder.EmbedAsync([question], cancellationToken).ConfigureAwait(false))[0];
        var candidates = new List<Observation>();
        await foreach (var (observation, _) in store.NearestAsync(vector, embedder.ModelId, 24, cancellationToken).ConfigureAwait(false))
        {
            candidates.Add(observation);
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        var order = await reranker.RankAsync(question, [.. candidates.Select(item => item.Body)], cancellationToken).ConfigureAwait(false);
        return [.. order.Take(6).Select(index => new LifeRecord(candidates[index].OccurredAt, candidates[index].Source, candidates[index].Body))];
    }
}

/// <summary>Receipts, by merchant or category; a money question sees the recent ones.</summary>
public sealed partial class ExpenseLifeSource(IExpenseLedger ledger) : ILifeSource
{
    private static readonly CultureInfo money = CultureInfo.GetCultureInfo("en-US");

    /// <inheritdoc />
    public async Task<IReadOnlyList<LifeRecord>> FindAsync(string question, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var spent = await ledger.BetweenAsync(today.AddDays(-400), today, cancellationToken).ConfigureAwait(false);
        var words = LifeWords.Of(question);
        var matching = spent.Where(item => LifeWords.Matches($"{item.Merchant} {item.Category}", words)).ToList();
        var chosen = matching.Count > 0 || !MoneyQuestion().IsMatch(question) ? matching : [.. spent];
        return [.. chosen.OrderByDescending(item => item.SpentOn).Take(10).Select(item => new LifeRecord(
            new DateTimeOffset(item.SpentOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), "receipt",
            $"{item.Merchant} {item.Total.ToString("C", money)} ({item.Category})"))];
    }

    [GeneratedRegex(@"\b(?:spend|spent|spending|paid|pay|cost|bought|buy|receipts?|price)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MoneyQuestion();
}

/// <summary>The calendar mirror, six months back and two ahead, by title or place.</summary>
public sealed class CalendarLifeSource(ICalendarStore calendar) : ILifeSource
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<LifeRecord>> FindAsync(string question, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var words = LifeWords.Of(question);
        var events = await calendar.BetweenAsync(now.AddDays(-180), now.AddDays(60), cancellationToken).ConfigureAwait(false);
        return [.. events.Where(item => LifeWords.Matches($"{item.Summary} {item.Location}", words)).Take(8)
            .Select(item => new LifeRecord(item.StartsAt, "calendar", item.Location is null ? item.Summary : $"{item.Summary} ({item.Location})"))];
    }
}

/// <summary>Forwarded mail as filed, by its reading.</summary>
public sealed class MailLifeSource(IDomainFactStore facts) : ILifeSource
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<LifeRecord>> FindAsync(string question, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var words = LifeWords.Of(question);
        var found = new List<LifeRecord>();
        await foreach (var fact in facts.TimelineAsync("mail", 300, cancellationToken).ConfigureAwait(false))
        {
            if (found.Count < 8 && LifeWords.Matches(fact.Description, words))
            {
                found.Add(new LifeRecord(new DateTimeOffset(fact.AsOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), "mail", fact.Description));
            }
        }

        return found;
    }
}

/// <summary>Meals: an eating question sees the last two weeks; otherwise by what was eaten.</summary>
public sealed partial class MealLifeSource(IMealLog meals) : ILifeSource
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<LifeRecord>> FindAsync(string question, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var eating = EatingQuestion().IsMatch(question);
        var logged = await meals.BetweenAsync(now.AddDays(eating ? -14 : -90), now, cancellationToken).ConfigureAwait(false);
        var words = LifeWords.Of(question);
        return [.. logged.Where(item => eating || LifeWords.Matches(item.Description, words))
            .OrderByDescending(item => item.EatenAt).Take(10)
            .Select(item => new LifeRecord(item.EatenAt, "meal", $"{item.Description}, about {item.Calories} kcal, {item.ProteinGrams} g protein"))];
    }

    [GeneratedRegex(@"\b(?:ate|eat|eaten|eating|meals?|calories|protein|food|lunch|dinner|breakfast)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EatingQuestion();
}

/// <summary>INR readings and doses, only for a question about anticoagulation (ADR-0037).</summary>
public sealed partial class AnticoagLifeSource(IAnticoagLog log) : ILifeSource
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<LifeRecord>> FindAsync(string question, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!AnticoagQuestion().IsMatch(question))
        {
            return [];
        }

        var readings = await log.ReadingsAsync(10, cancellationToken).ConfigureAwait(false);
        var doses = await log.DosesAsync(5, cancellationToken).ConfigureAwait(false);
        return [.. readings.Select(item => new LifeRecord(Day(item.OnDay), "INR", $"INR {item.Inr.ToString("0.0", CultureInfo.InvariantCulture)}"))
            .Concat(doses.Select(item => new LifeRecord(Day(item.OnDay), "dose", $"Dose: {item.Dose}")))];
    }

    private static DateTimeOffset Day(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    [GeneratedRegex(@"\b(?:inr|warfarin|coumadin|jantoven|doses?|anticoag\w*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnticoagQuestion();
}
