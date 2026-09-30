using System.Globalization;
using System.Text;
using Dami.Contracts.Memory;
using Dami.Contracts.Proactive;

namespace Dami.Proactive.Vault;

/// <summary>Weekly: the lessons that will lapse in the next two weeks, said before they do (B11).</summary>
/// <remarks>
/// Standing lessons lapse ninety days after Steve last said them (H20's guard against
/// Hermes' ever-growing, never-pruned self-improvement). A lapse he did not see coming is
/// a correction he has to give twice; this names the ones close to lapsing, and saying one
/// again keeps it. Silent on weeks with nothing close.
/// </remarks>
public sealed class LessonsReviewService : IProactiveService
{
    private const int READ = 200;
    private static readonly TimeSpan lifetime = TimeSpan.FromDays(90);
    private static readonly TimeSpan warning = TimeSpan.FromDays(14);

    private readonly IObservationCorpus corpus;
    private readonly TimeProvider clock;

    /// <summary>Creates the service.</summary>
    public LessonsReviewService(IObservationCorpus corpus, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(clock);
        this.corpus = corpus;
        this.clock = clock;
    }

    /// <summary>Each lesson's latest statement: saying one again renews it.</summary>
    private async Task<IEnumerable<Observation>> NewestAsync(CancellationToken cancellationToken)
    {
        var newest = new Dictionary<string, Observation>(StringComparer.OrdinalIgnoreCase);
        await foreach (var lesson in this.corpus.FromSourceAsync("lesson", READ, cancellationToken).ConfigureAwait(false))
        {
            var body = lesson.Body.Trim();
            if (!newest.TryGetValue(body, out var known) || lesson.OccurredAt > known.OccurredAt)
            {
                newest[body] = lesson;
            }
        }

        return newest.Values;
    }

    /// <inheritdoc />
    public string ServiceName => "lessons-review";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.Weekly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(ProactiveContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var now = this.clock.GetUtcNow();
        var lapsing = (await this.NewestAsync(cancellationToken).ConfigureAwait(false))
            .Select(lesson => (Lesson: lesson, Lapses: lesson.OccurredAt + lifetime))
            .Where(item => item.Lapses > now && item.Lapses <= now + warning)
            .OrderBy(item => item.Lapses)
            .ToList();
        if (lapsing.Count == 0)
        {
            return ProactiveResult.Did("no lesson lapses in the next two weeks");
        }

        var note = new StringBuilder("These lessons lapse soon unless you say them again:");
        foreach (var (lesson, lapses) in lapsing)
        {
            note.Append("\n• ").Append(lesson.Body.Trim()).Append(" (lapses ")
                .Append(lapses.ToString("MMM d", CultureInfo.InvariantCulture)).Append(')');
        }

        return new ProactiveResult(
            [], [new Surfacing(Guid.NewGuid(), this.ServiceName, $"{lapsing.Count} lesson(s) lapse soon", note.ToString(), 0.6, now)],
            ProactiveStatus.Completed, $"{lapsing.Count} lesson(s) lapse soon");
    }
}
