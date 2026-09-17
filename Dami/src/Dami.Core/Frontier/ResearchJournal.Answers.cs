using System.Runtime.CompilerServices;
using System.Text;
using Dami.Contracts.Research;

namespace Dami.Core.Frontier;

public sealed partial class ResearchJournal
{
    private const int MAX_ANSWER_CHARS = 64_000;

    /// <summary>Passes chat fragments through and archives the answer when this trace used research.</summary>
    public async IAsyncEnumerable<string> CaptureAsync(Guid traceId, IAsyncEnumerable<string> stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var answer = new StringBuilder();
        var completed = false;
        var truncated = false;
        try
        {
            await foreach (var fragment in stream.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                var length = Math.Min(fragment.Length, MAX_ANSWER_CHARS - answer.Length);
                answer.Append(fragment.AsSpan(0, length));
                truncated |= length < fragment.Length;
                yield return fragment;
            }

            completed = true;
        }
        finally
        {
            await this.SaveAnswerAsync(traceId, answer.ToString(), completed, truncated).ConfigureAwait(false);
        }
    }

    private async Task SaveAnswerAsync(Guid traceId, string answer, bool completed, bool truncated)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run = await this.store.FindByTraceAsync(traceId, cleanup.Token).ConfigureAwait(false);
        if (run is not null)
        {
            await this.store.SaveAsync(run with
            {
                Revision = run.Revision + 1,
                UpdatedAt = this.clock.GetUtcNow(),
                Answer = answer,
                AnswerStatus = completed && !truncated ? ResearchAnswerStatus.Complete : ResearchAnswerStatus.Partial,
                AnswerTruncated = truncated,
            }, cleanup.Token).ConfigureAwait(false);
        }
    }
}
