using System.Globalization;
using System.Text.Json;
using Dami.Contracts.Domains;
using Dami.Contracts.Models;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Proactive.Health;

/// <summary>Extracts structured health facts from observations into the health domain (K2).</summary>
/// <remarks>
/// LocalOnly by construction: it reads observations and the loopback model only, and
/// writes into a table with no egress path. It produces no surfacings — building the
/// timeline that D-007's cross-domain reflection reads is maintenance, not something to
/// interrupt Steve about. Every extracted row carries the observation it came from, so a
/// wrong extraction is traceable and correctable at its source.
/// </remarks>
public sealed class HealthCollectorService : IProactiveService
{
    private const string INSTRUCTIONS =
        """
        Extract health facts from the note below. Output a JSON array; each element is
        {"date":"YYYY-MM-DD","category":"diagnosis|appointment|medication|vital|procedure|symptom","description":"..."}.
        Use ONLY facts stated in the note. If the note contains no health information,
        output exactly []. Use the note's own dates; if a fact has no date, use the note
        date given. Keep each description to one clause. Output only the JSON array.
        """;

    private readonly IHealthEventStore healthStore;
    private readonly IChatClient chatClient;
    private readonly HealthCollectorOptions collectorOptions;
    private readonly ILogger<HealthCollectorService> logger;

    /// <summary>Creates the service.</summary>
    public HealthCollectorService(
        IHealthEventStore healthStore,
        IChatClient chatClient,
        IOptions<HealthCollectorOptions> collectorOptions,
        ILogger<HealthCollectorService> logger)
    {
        ArgumentNullException.ThrowIfNull(healthStore);
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(collectorOptions);
        ArgumentNullException.ThrowIfNull(logger);

        this.healthStore = healthStore;
        this.chatClient = chatClient;
        this.collectorOptions = collectorOptions.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ServiceName => "health-collector";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.Nightly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(
        ProactiveContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var pending = new List<(Guid ObservationId, DateOnly OccurredOn, string Body)>();
        await foreach (var row in this.healthStore
            .UnexaminedAsync(this.collectorOptions.BatchSize, cancellationToken).ConfigureAwait(false))
        {
            pending.Add(row);
        }

        var extracted = await this.ExamineAllAsync(pending, cancellationToken).ConfigureAwait(false);

        if (extracted > 0)
        {
            this.logger.LogInformation(
                "Health collector: {Count} fact(s) from {Examined} observation(s)",
                extracted, pending.Count);
        }

        return ProactiveResult.quiet;
    }

    private async Task<int> ExamineAllAsync(
        List<(Guid ObservationId, DateOnly OccurredOn, string Body)> pending,
        CancellationToken cancellationToken)
    {
        var extracted = 0;
        foreach (var observation in pending)
        {
            try
            {
                extracted += await this.ExamineAsync(observation, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A transient model or network hiccup on one note must not lose the whole
                // pass's progress. The note stays unexamined and is retried next pass.
                this.logger.LogWarning(
                    exception, "Health extraction failed for {Observation}; will retry next pass",
                    observation.ObservationId);
            }
        }

        return extracted;
    }

    private async Task<int> ExamineAsync(
        (Guid ObservationId, DateOnly OccurredOn, string Body) observation,
        CancellationToken cancellationToken)
    {
        var reply = await this.chatClient.CompleteAsync(
            $"{INSTRUCTIONS}\n\nNote date: {observation.OccurredOn:yyyy-MM-dd}\nNote: {observation.Body}",
            cancellationToken).ConfigureAwait(false);

        var written = 0;
        foreach (var fact in ParseFacts(reply, observation.OccurredOn))
        {
            await this.healthStore.RecordAsync(
                new HealthEvent(Guid.NewGuid(), observation.ObservationId, fact.Date, fact.Category, fact.Description),
                cancellationToken).ConfigureAwait(false);
            written++;
        }

        await this.healthStore.MarkExaminedAsync(observation.ObservationId, cancellationToken)
            .ConfigureAwait(false);
        return written;
    }

    private static IEnumerable<(DateOnly Date, HealthCategory Category, string Description)> ParseFacts(
        string reply,
        DateOnly fallbackDate)
    {
        var document = ParseArray(reply);
        if (document is null)
        {
            yield break;
        }

        using (document)
        {
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (TryReadFact(element, fallbackDate, out var fact))
                {
                    yield return fact;
                }
            }
        }
    }

    private static JsonDocument? ParseArray(string reply)
    {
        var json = ExtractArray(reply);
        if (json is null)
        {
            return null;
        }

        try
        {
            var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                return document;
            }

            document.Dispose();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryReadFact(
        JsonElement element,
        DateOnly fallbackDate,
        out (DateOnly Date, HealthCategory Category, string Description) fact)
    {
        fact = default;
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("description", out var descriptionElement)
            || !element.TryGetProperty("category", out var categoryElement)
            || descriptionElement.GetString() is not { Length: > 0 } description
            || !Enum.TryParse<HealthCategory>(categoryElement.GetString(), ignoreCase: true, out var category))
        {
            return false;
        }

        var date = fallbackDate;
        if (element.TryGetProperty("date", out var dateElement)
            && DateOnly.TryParse(dateElement.GetString(), CultureInfo.InvariantCulture, out var parsed))
        {
            date = parsed;
        }

        fact = (date, category, description.Trim());
        return true;
    }

    private static string? ExtractArray(string reply)
    {
        var start = reply.IndexOf('[', StringComparison.Ordinal);
        var end = reply.LastIndexOf(']');
        return start >= 0 && end > start ? reply[start..(end + 1)] : null;
    }
}
