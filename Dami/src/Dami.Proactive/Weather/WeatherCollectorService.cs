using Dami.Contracts.Domains;
using Dami.Contracts.Events;
using Dami.Contracts.Memory;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Proactive.Weather;

/// <summary>Pulls the NWS forecast and alerts for home (H14, egress half).</summary>
/// <remarks>
/// This half holds the egress client and reads nothing personal: two fixed public URLs,
/// city-level. Daytime forecast periods land as facts for the local-only scorer;
/// Severe and Extreme alerts surface directly — a tornado watch is not conditioned on
/// anything about Steve. Each alert surfaces once.
/// </remarks>
public sealed class WeatherCollectorService : IProactiveService
{
    private const string DOMAIN = "weather";
    private const int KNOWN_LIMIT = 400;
    private const int MAX_SURFACINGS_PER_PASS = 3;

    private readonly IDomainFactStore store;
    private readonly IEgressClient egressClient;
    private readonly WeatherOptions weatherOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<WeatherCollectorService> logger;

    /// <summary>Creates the service.</summary>
    public WeatherCollectorService(
        IDomainFactStore store,
        IEgressClient egressClient,
        IOptions<WeatherOptions> weatherOptions,
        TimeProvider clock,
        ILogger<WeatherCollectorService> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(egressClient);
        ArgumentNullException.ThrowIfNull(weatherOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.store = store;
        this.egressClient = egressClient;
        this.weatherOptions = weatherOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ServiceName => "weather-collector";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.Nightly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(
        ProactiveContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var known = await this.KnownAsync(cancellationToken).ConfigureAwait(false);
        var surfacings = new List<Surfacing>();
        var written = await this.ForecastPassAsync(known, context, cancellationToken).ConfigureAwait(false);
        written += await this.AlertPassAsync(known, surfacings, context, cancellationToken)
            .ConfigureAwait(false);

        this.logger.LogInformation(
            "Weather collector: {Written} new fact(s), {Surfaced} surfaced", written, surfacings.Count);
        return surfacings.Count == 0
            ? ProactiveResult.Did($"{written} new weather fact(s)")
            : new ProactiveResult(
                Array.Empty<Conclusion>(), surfacings, ProactiveStatus.Completed,
                $"{written} new weather fact(s), {surfacings.Count} alert(s) surfaced");
    }

    private async Task<int> ForecastPassAsync(
        HashSet<string> known, ProactiveContext context, CancellationToken cancellationToken)
    {
        try
        {
            var response = await this.FetchAsync(
                this.weatherOptions.ForecastUrl, "NWS forecast", context, cancellationToken)
                .ConfigureAwait(false);
            var written = 0;
            var horizon = this.clock.GetUtcNow().AddDays(Math.Max(1, this.weatherOptions.ForecastDays));
            foreach (var period in WeatherFeeds.ParseForecast(response.Body))
            {
                if (!period.IsDaytime || period.Start > horizon)
                {
                    continue;
                }

                written += await this.RecordAsync(
                    known, "forecast", DateOnly.FromDateTime(period.Start.Date),
                    WeatherFeeds.ForecastDescription(period), cancellationToken)
                    .ConfigureAwait(false)
                    ? 1
                    : 0;
            }

            return written;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            this.logger.LogWarning(exception, "Forecast fetch failed; continuing");
            return 0;
        }
    }

    private async Task<int> AlertPassAsync(
        HashSet<string> known,
        List<Surfacing> surfacings,
        ProactiveContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await this.FetchAsync(
                this.weatherOptions.AlertsUrl, "NWS alerts", context, cancellationToken)
                .ConfigureAwait(false);
            var written = 0;
            foreach (var alert in WeatherFeeds.ParseAlerts(response.Body))
            {
                if (alert.Severity is not ("Severe" or "Extreme"))
                {
                    continue;
                }

                var today = DateOnly.FromDateTime(this.clock.GetUtcNow().UtcDateTime);
                if (await this.RecordAsync(known, "alert", today, $"alert: {alert.Headline}", cancellationToken)
                    .ConfigureAwait(false))
                {
                    written++;
                    this.TrySurface(surfacings, alert);
                }
            }

            return written;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            this.logger.LogWarning(exception, "Alerts fetch failed; continuing");
            return 0;
        }
    }

    private void TrySurface(List<Surfacing> surfacings, WeatherAlert alert)
    {
        if (surfacings.Count < MAX_SURFACINGS_PER_PASS)
        {
            surfacings.Add(new Surfacing(
                Guid.NewGuid(), this.ServiceName, alert.Event, alert.Headline,
                this.weatherOptions.Confidence, this.clock.GetUtcNow()));
        }
    }

    private async Task<EgressResponse> FetchAsync(
        string url, string purpose, ProactiveContext context, CancellationToken cancellationToken)
    {
        return await this.egressClient.SendAsync(
            new EgressRequest(new Uri(url), purpose, context.TraceId, ExecutionOrigin.ScheduledService),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> RecordAsync(
        HashSet<string> known,
        string category,
        DateOnly asOf,
        string description,
        CancellationToken cancellationToken)
    {
        if (known.Contains(description))
        {
            return false;
        }

        var recorded = await this.store.RecordAsync(
            new DomainFact(
                Guid.NewGuid(), DOMAIN, asOf, category, description,
                this.ServiceName, this.clock.GetUtcNow()),
            cancellationToken).ConfigureAwait(false);
        if (recorded)
        {
            known.Add(description);
        }

        return recorded;
    }

    private async Task<HashSet<string>> KnownAsync(CancellationToken cancellationToken)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var fact in this.store
            .TimelineAsync(DOMAIN, KNOWN_LIMIT, cancellationToken).ConfigureAwait(false))
        {
            known.Add(fact.Description);
        }

        return known;
    }
}
