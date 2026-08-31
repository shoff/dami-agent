using System.Globalization;
using Dami.Contracts.Domains;
using Dami.Contracts.Memory;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Proactive.Weather;

/// <summary>Scores tomorrow against the training log, locally (H14, local half).</summary>
/// <remarks>
/// This half reads the fitness domain and therefore, by the recorded D-012 rule, holds
/// no egress client — the training habits it derives cannot leave. It reads the
/// forecast facts the collector recorded, judges tomorrow with stated thresholds, and
/// surfaces a good window once per day. A bad day, or no forecast, produces nothing.
/// </remarks>
public sealed class WeatherWindowService : IProactiveService
{
    private const string DOMAIN = "weather";
    private const string WINDOW_CATEGORY = "window";
    private const int KNOWN_LIMIT = 400;

    private readonly IFitnessStore fitness;
    private readonly IDomainFactStore store;
    private readonly WeatherOptions weatherOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<WeatherWindowService> logger;

    /// <summary>Creates the service.</summary>
    public WeatherWindowService(
        IFitnessStore fitness,
        IDomainFactStore store,
        IOptions<WeatherOptions> weatherOptions,
        TimeProvider clock,
        ILogger<WeatherWindowService> logger)
    {
        ArgumentNullException.ThrowIfNull(fitness);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(weatherOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.fitness = fitness;
        this.store = store;
        this.weatherOptions = weatherOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ServiceName => "weather-window";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.Nightly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(
        ProactiveContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var tomorrow = this.Tomorrow();
        var (forecast, known) = await this.ForecastForAsync(tomorrow, cancellationToken)
            .ConfigureAwait(false);
        if (forecast is null || known.Contains(WindowDescription(tomorrow)))
        {
            return ProactiveResult.quiet;
        }

        var (good, why) = CardioWindows.Judge(
            forecast.Value.TemperatureF, forecast.Value.WindMph, forecast.Value.PrecipPct);
        if (!good)
        {
            return ProactiveResult.Did($"tomorrow is not a window ({why})");
        }

        return await this.SurfaceAsync(tomorrow, why, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProactiveResult> SurfaceAsync(
        DateOnly tomorrow, string why, CancellationToken cancellationToken)
    {
        var hour = await this.UsualHourAsync(cancellationToken).ConfigureAwait(false);
        var recorded = await this.store.RecordAsync(
            new DomainFact(
                Guid.NewGuid(), DOMAIN, tomorrow, WINDOW_CATEGORY, WindowDescription(tomorrow),
                this.ServiceName, this.clock.GetUtcNow()),
            cancellationToken).ConfigureAwait(false);
        if (!recorded)
        {
            return ProactiveResult.quiet;
        }

        var when = hour is null
            ? "during daylight"
            : string.Create(CultureInfo.InvariantCulture, $"around your usual {hour:00}:00");
        var surfacing = new Surfacing(
            Guid.NewGuid(), this.ServiceName,
            "Tomorrow looks good for outdoor cardio",
            $"{why} — {when}.",
            this.weatherOptions.Confidence, this.clock.GetUtcNow());
        return new ProactiveResult(
            Array.Empty<Conclusion>(), [surfacing], ProactiveStatus.Completed,
            $"window surfaced for {tomorrow:yyyy-MM-dd}");
    }

    private async Task<int?> UsualHourAsync(CancellationToken cancellationToken)
    {
        var snapshot = await this.fitness.SnapshotAsync(cancellationToken).ConfigureAwait(false);
        var times = new List<DateTimeOffset>(snapshot.Cardio.Count);
        foreach (var session in snapshot.Cardio)
        {
            times.Add(session.OccurredAt);
        }

        return CardioWindows.UsualHour(times, this.weatherOptions.LocalUtcOffsetHours);
    }

    /// <summary>The newest forecast fact for the date, and every window already surfaced.</summary>
    private async Task<((DateOnly Date, int TemperatureF, int WindMph, int PrecipPct)? Forecast, HashSet<string> Known)>
        ForecastForAsync(DateOnly date, CancellationToken cancellationToken)
    {
        (DateOnly, int, int, int)? forecast = null;
        var known = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var fact in this.store
            .TimelineAsync(DOMAIN, KNOWN_LIMIT, cancellationToken).ConfigureAwait(false))
        {
            if (fact.Category == WINDOW_CATEGORY)
            {
                known.Add(fact.Description);
                continue;
            }

            var parsed = WeatherFeeds.ReadForecastFact(fact.Description);
            if (forecast is null && parsed is not null && parsed.Value.Date == date)
            {
                forecast = parsed;
            }
        }

        return (forecast, known);
    }

    private DateOnly Tomorrow()
    {
        var local = this.clock.GetUtcNow().AddHours(this.weatherOptions.LocalUtcOffsetHours);
        return DateOnly.FromDateTime(local.UtcDateTime).AddDays(1);
    }

    private static string WindowDescription(DateOnly date) =>
        string.Create(CultureInfo.InvariantCulture, $"cardio-window {date:yyyy-MM-dd}");
}
