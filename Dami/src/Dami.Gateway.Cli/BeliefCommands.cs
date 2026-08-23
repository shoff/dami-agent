using Dami.Contracts.Memory;

namespace Dami.Gateway.Cli;

/// <summary>The ledger, readable and correctable.</summary>
/// <remarks>
/// The register's success definition, verbatim: Steve "can open the ledger, see exactly
/// why Dami thought it, and correct it if it is wrong." This is that opening. The diff
/// is D-011's second instrument — drift toward flattery visible as text.
/// </remarks>
public sealed class BeliefCommands
{
    private readonly IConclusionLedger conclusionLedger;
    private readonly IObservationCorpus observationCorpus;
    private readonly TimeProvider clock;

    /// <summary>Creates the commands.</summary>
    public BeliefCommands(
        IConclusionLedger conclusionLedger,
        IObservationCorpus observationCorpus,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(conclusionLedger);
        ArgumentNullException.ThrowIfNull(observationCorpus);
        ArgumentNullException.ThrowIfNull(clock);

        this.conclusionLedger = conclusionLedger;
        this.observationCorpus = observationCorpus;
        this.clock = clock;
    }

    /// <summary>Prints the currently believed set, or the set as of a date.</summary>
    public async Task<int> ListAsync(string? asOf, CancellationToken cancellationToken)
    {
        var moment = this.clock.GetUtcNow();
        if (asOf is not null && !DateTimeOffset.TryParse(asOf, out moment))
        {
            await Console.Error.WriteLineAsync($"'{asOf}' is not a date").ConfigureAwait(false);
            return 1;
        }

        var any = false;
        await foreach (var conclusion in this.conclusionLedger.ActiveAsOfAsync(moment, cancellationToken)
            .ConfigureAwait(false))
        {
            any = true;
            Print(conclusion);
        }

        if (!any)
        {
            Console.WriteLine("the ledger holds no active conclusions" + (asOf is null ? "" : $" as of {asOf}"));
        }

        return 0;
    }

    /// <summary>Prints what changed between two moments — the drift instrument.</summary>
    public async Task<int> DiffAsync(string from, string? to, CancellationToken cancellationToken)
    {
        if (!DateTimeOffset.TryParse(from, out var fromMoment))
        {
            await Console.Error.WriteLineAsync($"'{from}' is not a date").ConfigureAwait(false);
            return 1;
        }

        var toMoment = this.clock.GetUtcNow();
        if (to is not null && !DateTimeOffset.TryParse(to, out toMoment))
        {
            await Console.Error.WriteLineAsync($"'{to}' is not a date").ConfigureAwait(false);
            return 1;
        }

        var before = await this.CollectAsync(fromMoment, cancellationToken).ConfigureAwait(false);
        var after = await this.CollectAsync(toMoment, cancellationToken).ConfigureAwait(false);
        PrintDiff(before, after);
        return 0;
    }

    /// <summary>Retracts a belief — the correction taking effect.</summary>
    public async Task<int> RetractAsync(string idPrefix, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reason);

        var target = await this.ResolveAsync(idPrefix, cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            await Console.Error.WriteLineAsync($"no active conclusion matches '{idPrefix}'").ConfigureAwait(false);
            return 1;
        }

        await this.conclusionLedger
            .RetractAsync(target.ConclusionId, reason, this.clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine($"retracted: {target.Statement}");
        Console.WriteLine($"  reason: {reason}");
        return 0;
    }

    /// <summary>Replaces a belief with a corrected one — supersession, not deletion.</summary>
    /// <remarks>
    /// F-10: corrections supersede rather than coexist. The old belief stays in the
    /// ledger, retracted with the correction chain pointing at its replacement, so
    /// "why did Dami stop believing that" always has an answer.
    /// </remarks>
    public async Task<int> CorrectAsync(
        string idPrefix,
        string correctedStatement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(correctedStatement);

        var target = await this.ResolveAsync(idPrefix, cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            await Console.Error.WriteLineAsync($"no active conclusion matches '{idPrefix}'").ConfigureAwait(false);
            return 1;
        }

        var replacement = new Conclusion(
            Guid.NewGuid(),
            target.ConclusionId,
            target.Subject,
            correctedStatement,
            1.0,
            ConclusionSource.Correction,
            this.clock.GetUtcNow(),
            target.SupportingObservations);

        await this.conclusionLedger
            .SupersedeAsync(replacement, "corrected by Steve", cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine($"was:    {target.Statement}");
        Console.WriteLine($"now:    {replacement.Statement}");
        Console.WriteLine($"        (confidence 1.00 - a direct correction outranks any inference)");
        return 0;
    }

    /// <summary>Records an observation from the command line into the corpus.</summary>
    public async Task<int> NoteAsync(string body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var observation = new Observation(
            Guid.NewGuid(), this.clock.GetUtcNow(), "cli-note", body);
        await this.observationCorpus.RecordAsync(observation, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"noted ({Short(observation.ObservationId)})");
        return 0;
    }

    private async Task<Dictionary<Guid, Conclusion>> CollectAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var set = new Dictionary<Guid, Conclusion>();
        await foreach (var conclusion in this.conclusionLedger.ActiveAsOfAsync(asOf, cancellationToken)
            .ConfigureAwait(false))
        {
            set[conclusion.ConclusionId] = conclusion;
        }

        return set;
    }

    private static void PrintDiff(Dictionary<Guid, Conclusion> before, Dictionary<Guid, Conclusion> after)
    {
        var changed = false;

        foreach (var (id, conclusion) in after)
        {
            if (!before.ContainsKey(id))
            {
                changed = true;
                Console.WriteLine($"+ {conclusion.Statement}");
            }
        }

        foreach (var (id, conclusion) in before)
        {
            if (!after.ContainsKey(id))
            {
                changed = true;
                Console.WriteLine($"- {conclusion.Statement}  [{conclusion.RetractionReason ?? "superseded"}]");
            }
        }

        if (!changed)
        {
            Console.WriteLine("no drift: the believed set is unchanged");
        }
    }

    private async Task<Conclusion?> ResolveAsync(string idPrefix, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(idPrefix);
        var normalized = idPrefix.Replace("-", "", StringComparison.Ordinal);

        await foreach (var conclusion in this.conclusionLedger
            .ActiveAsOfAsync(this.clock.GetUtcNow(), cancellationToken).ConfigureAwait(false))
        {
            if (conclusion.ConclusionId.ToString("N").StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            {
                return conclusion;
            }
        }

        return null;
    }

    private static void Print(Conclusion conclusion)
    {
        var provenance = conclusion.SupportingObservations.Count > 0
            ? $"{conclusion.SupportingObservations.Count} obs"
            : "no provenance";
        Console.WriteLine(
            $"{Short(conclusion.ConclusionId)}  {conclusion.Confidence:0.00}  [{conclusion.Source}, {provenance}]  {conclusion.Statement}");
    }

    private static string Short(Guid id)
    {
        return id.ToString("N")[..8];
    }
}
