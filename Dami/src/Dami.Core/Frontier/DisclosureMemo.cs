using System.Collections.Concurrent;
using Dami.Contracts.Privacy;

namespace Dami.Core.Frontier;

/// <summary>Remembers the gate's verdict on a line for a while, so the same line is not judged every turn.</summary>
/// <remarks>
/// The conversation history the gateway sends is the same dozen lines turn after turn,
/// and each turn asked the local model to rule on all of them again. A verdict on a
/// given line is good for a bounded time; a correction Steve makes forgets it at once.
/// Process-local and bounded: it is a cache, not a record — the ledger is the record.
/// </remarks>
public sealed class DisclosureMemo
{
    private const int CAPACITY = 2000;

    private readonly ConcurrentDictionary<string, (DisclosedItem Item, DateTimeOffset At)> verdicts =
        new(StringComparer.Ordinal);

    private readonly TimeProvider clock;

    /// <summary>Creates the memo.</summary>
    public DisclosureMemo(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        this.clock = clock;
    }

    /// <summary>A verdict still within its lifetime, or null.</summary>
    public DisclosedItem? Recall(string line, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (lifetime <= TimeSpan.Zero || !this.verdicts.TryGetValue(line, out var entry))
        {
            return null;
        }

        if (this.clock.GetUtcNow() - entry.At > lifetime)
        {
            this.verdicts.TryRemove(line, out _);
            return null;
        }

        return entry.Item;
    }

    /// <summary>Keeps fresh verdicts.</summary>
    public void Remember(IEnumerable<DisclosedItem> decided)
    {
        ArgumentNullException.ThrowIfNull(decided);
        var now = this.clock.GetUtcNow();
        foreach (var item in decided)
        {
            this.verdicts[item.Original] = (item, now);
        }

        if (this.verdicts.Count > CAPACITY)
        {
            foreach (var stale in this.verdicts.OrderBy(pair => pair.Value.At).Take(this.verdicts.Count - CAPACITY).ToList())
            {
                this.verdicts.TryRemove(stale.Key, out _);
            }
        }
    }

    /// <summary>Drops a verdict — a correction from Steve must take effect on the next turn.</summary>
    public void Forget(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        this.verdicts.TryRemove(line, out _);
    }

    /// <summary>
    /// Judges only what is not remembered, records only that, and hands back every line in
    /// its original order. The gate call shrinks to the genuinely new lines of a turn.
    /// </summary>
    public async Task<IReadOnlyList<DisclosedItem>> DecideAsync(
        IReadOnlyList<string> lines,
        TimeSpan lifetime,
        Func<IReadOnlyList<string>, Task<IReadOnlyList<DisclosedItem>>> judge,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(judge);
        cancellationToken.ThrowIfCancellationRequested();
        var remembered = new Dictionary<string, DisclosedItem>(StringComparer.Ordinal);
        var fresh = new List<string>();
        foreach (var line in lines)
        {
            if (this.Recall(line, lifetime) is { } known)
            {
                remembered[line] = known;
            }
            else if (!fresh.Contains(line, StringComparer.Ordinal))
            {
                fresh.Add(line);
            }
        }

        if (fresh.Count > 0)
        {
            var decided = await judge(fresh).ConfigureAwait(false);
            this.Remember(decided);
            foreach (var item in decided)
            {
                remembered[item.Original] = item;
            }
        }

        return [.. lines.Select(line => remembered.TryGetValue(line, out var item)
            ? item
            : new DisclosedItem(line, Disclosure.Withhold, string.Empty, "gate returned no verdict"))];
    }
}
