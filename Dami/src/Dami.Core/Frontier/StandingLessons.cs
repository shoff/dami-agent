using Dami.Contracts.Memory;
using Microsoft.Extensions.Logging;

namespace Dami.Core.Frontier;

/// <summary>The lessons the frontier is told on every turn.</summary>
public interface IStandingLessons
{
    /// <summary>The lessons, newest first, deduplicated; empty rather than failing.</summary>
    Task<IReadOnlyList<string>> LinesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reads the <c>lesson</c> observations and hands them to whoever builds a frontier prompt.
/// They travel as local context, so the disclosure gate judges them like any other
/// profile-derived line before they leave the host (D-012).
/// </summary>
public sealed class StandingLessons : IStandingLessons
{
    private const int LIMIT = 20;

    private readonly IObservationCorpus corpus;
    private readonly ILogger<StandingLessons> logger;

    /// <summary>Creates the reader.</summary>
    public StandingLessons(IObservationCorpus corpus, ILogger<StandingLessons> logger)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(logger);
        this.corpus = corpus;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> LinesAsync(CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await foreach (var observation in this.corpus.FromSourceAsync(LessonTool.SOURCE, LIMIT, cancellationToken)
                .ConfigureAwait(false))
            {
                var body = observation.Body.Trim();
                if (body.Length > 0 && seen.Add(body))
                {
                    lines.Add(body);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A turn without its lessons is worse than one that never happens? No: answer, and say so in the log.
            this.logger.LogWarning(exception, "Could not read the standing lessons; answering without them");
            return [];
        }

        return lines;
    }
}
