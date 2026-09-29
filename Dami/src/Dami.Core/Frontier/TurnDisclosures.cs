using System.Collections.Concurrent;
using Dami.Contracts.Privacy;

namespace Dami.Core.Frontier;

/// <summary>
/// Every local line a recent turn put before the disclosure gate, with its verdict, by trace.
/// </summary>
/// <remarks>
/// What "why did you say that?" is answered from (docs/agent-landscape-2026-09.md B1). The
/// ledger cannot answer it: <see cref="DisclosureMemo"/> reuses a verdict for thirty
/// minutes, and a reused verdict is not recorded again under the new turn. Process-local and
/// bounded — the question is always about a turn that just happened.
/// </remarks>
public sealed class TurnDisclosures
{
    /// <summary>How many recent turns are kept.</summary>
    public const int KEPT = 64;

    private readonly ConcurrentDictionary<Guid, IReadOnlyList<DisclosedItem>> turns = new();
    private readonly ConcurrentQueue<Guid> order = new();

    /// <summary>Keeps one turn's verdicts, dropping the oldest turn past <see cref="KEPT"/>.</summary>
    public void Remember(Guid traceId, IReadOnlyList<DisclosedItem> decided)
    {
        ArgumentNullException.ThrowIfNull(decided);
        if (this.turns.TryAdd(traceId, decided))
        {
            this.order.Enqueue(traceId);
        }

        while (this.order.Count > KEPT && this.order.TryDequeue(out var oldest))
        {
            this.turns.TryRemove(oldest, out _);
        }
    }

    /// <summary>The verdicts of one turn, or null when it is not a recent one.</summary>
    public IReadOnlyList<DisclosedItem>? For(Guid traceId) =>
        this.turns.TryGetValue(traceId, out var decided) ? decided : null;
}
