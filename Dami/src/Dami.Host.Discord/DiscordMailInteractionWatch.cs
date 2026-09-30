using Dami.Contracts.Domains;
using Dami.Gateway.Discord;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dami.Host.Discord;

/// <summary>ADR-0037 slice 3: a warfarin interactor in newly filed forwarded mail, noticed in the DM.</summary>
/// <remarks>
/// The mailbox collector files each forwarded email as a one-sentence reading that names any
/// medicine it mentions; this looks at readings filed since it last looked. A first run looks
/// back two days only, so old mail is never swept up into a burst of notes.
/// </remarks>
public sealed class DiscordMailInteractionWatch : BackgroundService
{
    private const int READ = 100;
    private static readonly TimeSpan every = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan firstLook = TimeSpan.FromDays(2);

    private readonly IDomainFactStore facts;
    private readonly DiscordInteractionWatch watch;
    private readonly DiscordOptions options;
    private readonly TimeProvider clock;
    private readonly ILogger<DiscordMailInteractionWatch> logger;
    private readonly DayMarker marker;

    /// <summary>Creates the watch, remembering where it got to under <c>~/.local/state/dami</c>.</summary>
    public DiscordMailInteractionWatch(
        IDomainFactStore facts, DiscordInteractionWatch watch, DiscordOptions options, TimeProvider clock,
        ILogger<DiscordMailInteractionWatch> logger)
        : this(facts, watch, options, clock, DayMarker.Default("anticoag-mail"), logger)
    {
    }

    /// <summary>Creates the watch with the file that remembers where it got to.</summary>
    public DiscordMailInteractionWatch(
        IDomainFactStore facts, DiscordInteractionWatch watch, DiscordOptions options, TimeProvider clock, string statePath,
        ILogger<DiscordMailInteractionWatch> logger)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(watch);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        (this.facts, this.watch, this.options, this.clock, this.logger) = (facts, watch, options, clock, logger);
        this.marker = new DayMarker(statePath, logger);
    }

    /// <summary>Looks at mail filed since the last look.</summary>
    public async Task TickAsync(CancellationToken cancellationToken)
    {
        if (this.options.CheckInConversationId.Length == 0)
        {
            return;
        }

        var now = this.clock.GetUtcNow();
        var last = this.marker.Read();
        var since = last > DateTimeOffset.MinValue ? last : now - firstLook;
        await foreach (var fact in this.facts.TimelineAsync("mail", READ, cancellationToken).ConfigureAwait(false))
        {
            if (fact.RecordedAt > since)
            {
                await this.watch.NoticeAsync(
                    this.options.CheckInConversationId, fact.Description, $"in forwarded mail (“{fact.Description}”)", cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        this.marker.Write(now);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await this.TickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                this.logger.LogWarning(exception, "Mail interaction watch tick failed");
            }

            await Task.Delay(every, this.clock, stoppingToken).ConfigureAwait(false);
        }
    }
}
