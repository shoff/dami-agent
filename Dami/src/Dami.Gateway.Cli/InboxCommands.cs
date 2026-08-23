using Dami.Contracts.Memory;
using Dami.Contracts.Proactive;

namespace Dami.Gateway.Cli;

/// <summary>The inbox: list, read, and react.</summary>
/// <remarks>
/// Feedback is the point. D-019 built the scout first because the reaction trains the
/// taste model every later service depends on, and this is where the reaction enters.
/// </remarks>
public sealed class InboxCommands
{
    private const int LIST_LIMIT = 20;

    private readonly ISurfacingQueue surfacingQueue;
    private readonly IObservationCorpus observationCorpus;
    private readonly TimeProvider clock;

    /// <summary>Creates the commands.</summary>
    public InboxCommands(
        ISurfacingQueue surfacingQueue,
        IObservationCorpus observationCorpus,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(surfacingQueue);
        ArgumentNullException.ThrowIfNull(observationCorpus);
        ArgumentNullException.ThrowIfNull(clock);

        this.surfacingQueue = surfacingQueue;
        this.observationCorpus = observationCorpus;
        this.clock = clock;
    }

    /// <summary>Lists pending surfacings.</summary>
    public async Task<int> ListPendingAsync(CancellationToken cancellationToken)
    {
        var any = false;

        await foreach (var surfacing in this.surfacingQueue.PendingAsync(LIST_LIMIT, cancellationToken)
            .ConfigureAwait(false))
        {
            any = true;
            Print(surfacing);
        }

        if (!any)
        {
            Console.WriteLine("nothing pending - the muse is quiet");
        }

        return 0;
    }

    /// <summary>Lists recent surfacings in every status.</summary>
    public async Task<int> ListRecentAsync(CancellationToken cancellationToken)
    {
        await foreach (var surfacing in this.surfacingQueue.RecentAsync(LIST_LIMIT, cancellationToken)
            .ConfigureAwait(false))
        {
            Print(surfacing);
        }

        return 0;
    }

    /// <summary>Shows one surfacing in full and marks it delivered.</summary>
    public async Task<int> ReadAsync(string idPrefix, CancellationToken cancellationToken)
    {
        var surfacing = await this.ResolveAsync(idPrefix, cancellationToken).ConfigureAwait(false);
        if (surfacing is null)
        {
            await Console.Error.WriteLineAsync($"no surfacing matches '{idPrefix}'").ConfigureAwait(false);
            return 1;
        }

        Console.WriteLine($"{surfacing.Title}");
        Console.WriteLine($"  {surfacing.Body}");
        Console.WriteLine($"  from {surfacing.ServiceName}, confidence {surfacing.Confidence:0.00}");
        Console.WriteLine($"  react with: dami good|bad|meh {Short(surfacing.SurfacingId)} [note]");

        await this.surfacingQueue
            .DeliverAsync(surfacing.SurfacingId, this.clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
        return 0;
    }

    /// <summary>Records a reaction.</summary>
    public async Task<int> FeedbackAsync(
        string idPrefix,
        string verdict,
        string? note,
        CancellationToken cancellationToken)
    {
        var surfacing = await this.ResolveAsync(idPrefix, cancellationToken).ConfigureAwait(false);
        if (surfacing is null)
        {
            await Console.Error.WriteLineAsync($"no surfacing matches '{idPrefix}'").ConfigureAwait(false);
            return 1;
        }

        var feedback = note is null ? verdict : $"{verdict}: {note}";
        var reactedAt = this.clock.GetUtcNow();
        await this.surfacingQueue
            .RecordFeedbackAsync(surfacing.SurfacingId, feedback, reactedAt, cancellationToken)
            .ConfigureAwait(false);

        // A reaction is itself something that happened, so it joins the corpus - which
        // is how the reflection pass gets to notice patterns in what Steve values.
        await this.observationCorpus.RecordAsync(
            new Observation(
                Guid.NewGuid(), reactedAt, "surfacing-feedback",
                $"rated the surfacing '{surfacing.Title}' {feedback}"),
            cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"recorded '{feedback}' on {Short(surfacing.SurfacingId)} - this trains the taste model");
        return 0;
    }

    private async Task<Surfacing?> ResolveAsync(string idPrefix, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(idPrefix);

        await foreach (var surfacing in this.surfacingQueue.RecentAsync(100, cancellationToken)
            .ConfigureAwait(false))
        {
            if (surfacing.SurfacingId.ToString("N").StartsWith(
                idPrefix.Replace("-", "", StringComparison.Ordinal),
                StringComparison.OrdinalIgnoreCase))
            {
                return surfacing;
            }
        }

        return null;
    }

    private static void Print(Surfacing surfacing)
    {
        Console.WriteLine($"{Short(surfacing.SurfacingId)}  {surfacing.Confidence:0.00}  {surfacing.Title}");
    }

    private static string Short(Guid id)
    {
        return id.ToString("N")[..8];
    }
}
