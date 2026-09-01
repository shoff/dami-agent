using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Memory;
using Dami.Contracts.Models;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Proactive.Portrait;

/// <summary>The daily portrait, ported from the Hermes cron jobs (ADR-0027).</summary>
/// <remarks>
/// Three passes a day, each writing one image to this host and surfacing that it exists.
/// It does not deliver: the surfacing queue is canonical and whether anything pushes
/// outward is ADR-0014, still unsigned. Steve sees it in the inbox until he decides that.
///
/// The prompt is composed here from configuration and carries nothing retrieved, which is
/// why it is Egressable. Nothing about the profile reaches the image provider — the only
/// thing that leaves is a sentence Steve wrote himself.
///
/// Deliberately idempotent per slot: a restart, or a pass that runs twice inside its
/// window, must not buy the same picture twice from a metered API.
/// </remarks>
public sealed class DailyPortraitService : IProactiveService
{
    private readonly IImageGenerator generator;
    private readonly DailyPortraitOptions portraitOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<DailyPortraitService> logger;

    /// <summary>Creates the service.</summary>
    public DailyPortraitService(
        IImageGenerator generator,
        IOptions<DailyPortraitOptions> portraitOptions,
        TimeProvider clock,
        ILogger<DailyPortraitService> logger)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(portraitOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.generator = generator;
        this.portraitOptions = portraitOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ServiceName => "daily-portrait";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.EightHourly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(
        ProactiveContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!this.portraitOptions.Enabled)
        {
            return ProactiveResult.quiet;
        }

        var now = this.clock.GetUtcNow();
        var offset = this.portraitOptions.LocalUtcOffsetHours;
        var slot = PortraitSlot.Of(now, offset);
        var path = Path.Combine(
            this.portraitOptions.OutputDirectory, PortraitSlot.FileNameFor(now, offset));
        if (File.Exists(path))
        {
            return ProactiveResult.Did($"{slot} portrait already exists");
        }

        return await this.RenderAsync(context, slot, path, now, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ProactiveResult> RenderAsync(
        ProactiveContext context,
        string slot,
        string path,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            var image = await this.generator
                .GenerateAsync(this.RequestFor(slot, context.TraceId), cancellationToken)
                .ConfigureAwait(false);

            Directory.CreateDirectory(this.portraitOptions.OutputDirectory);
            await File.WriteAllBytesAsync(path, image.Bytes.ToArray(), cancellationToken)
                .ConfigureAwait(false);
            this.logger.LogInformation("Daily portrait: wrote the {Slot} image to {Path}", slot, path);

            return new ProactiveResult(
                Array.Empty<Conclusion>(),
                [new Surfacing(
                    Guid.NewGuid(), this.ServiceName,
                    $"Today's {slot} portrait", path,
                    this.portraitOptions.Confidence, now)],
                ProactiveStatus.Completed,
                $"{slot} portrait written");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A refused host, a missing key, a rate limit — the Hermes jobs died on all
            // three and said so only in a log nobody read. This completes and reports.
            this.logger.LogWarning(exception, "Daily portrait ({Slot}) could not be produced", slot);
            return ProactiveResult.Did($"{slot} portrait not produced: {exception.Message}");
        }
    }

    /// <summary>
    /// The request. Egressable because this prompt is composed from configuration and
    /// carries nothing retrieved — the generator refuses anything that is not.
    /// </summary>
    private ImageRequest RequestFor(string slot, Guid traceId) =>
        new(
            this.portraitOptions.PromptTemplate.Replace("{slot}", slot, StringComparison.Ordinal),
            $"daily portrait ({slot})",
            PrivacyClass.Egressable,
            traceId,
            ExecutionOrigin.ScheduledService)
        {
            Size = this.portraitOptions.Size,
            Quality = this.portraitOptions.Quality,
        };
}
