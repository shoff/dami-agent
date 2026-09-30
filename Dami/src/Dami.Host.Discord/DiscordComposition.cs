using Dami.Contracts.Privacy;
using Dami.Core.Reliability;
using Dami.Gateway.Discord;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dami.Host.Discord;

/// <summary>Registers the Discord gateway (ADR-0024, M1).</summary>
/// <remarks>
/// A separate method rather than lines in the host's top-level statements, because
/// D-012's guarantee is that the set of things able to reach off the host is knowable by
/// reading the composition root. Registrations scattered through a Program file are how
/// that stops being true — and how <c>LanScanner</c> was left unregistered until the
/// proactive tier crash-looped in production.
/// </remarks>
public static class DiscordComposition
{
    /// <summary>Adds the gateway, which stays dormant unless it is configured.</summary>
    public static IServiceCollection AddDamiDiscordGateway(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = Read(configuration);
        services.AddSingleton(options);

        if (!options.IsConfigured)
        {
            // Say so rather than registering nothing: silence at startup reads exactly
            // like a healthy gateway with nothing to report.
            services.AddHostedService<DiscordDisabledNotice>();
            return services;
        }

        AddTransport(services, options);
        AddAnswering(services, configuration);

        services.AddHostedService<DiscordGatewayWorker>();
        // Once a day, the strongest pending surfacing to Steve's DM (ADR-0014 as amended 2026-09-29).
        services.AddHostedService<DiscordDailyCheckIn>();
        // What failed without anyone noticing, named in the DM (docs/agent-landscape-2026-09.md A1).
        services.AddSingleton<WeeklyActivity>();
        services.AddHostedService<DiscordReliabilityNotice>();
        // A sidecar failing real work twice in a row, said within minutes (A4).
        services.AddHostedService<DiscordOutageAlarm>();
        // ADR-0037 slice 2: the Monday anticoagulation note, DM only.
        services.AddHostedService<DiscordAnticoagWeekly>();
        // ADR-0037 slice 3: interactors in newly filed forwarded mail.
        services.AddHostedService<DiscordMailInteractionWatch>();
        return services;
    }

    private static void AddAnswering(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AnticoagOptions>(configuration.GetSection(AnticoagOptions.SECTION));
        // The local vision model reads what Steve sends; the caption becomes context and
        // the image itself never leaves this host (ADR-0026).
        services.AddSingleton<DiscordVision>();
        // Voice notes are transcribed on this host (L3); only the words go on.
        services.AddSingleton<DiscordHearing>();
        // Receipts are read and logged on this host; the frontier never sees them (D4).
        services.AddSingleton<DiscordReceiptResponder>();
        services.AddSingleton<DiscordMealResponder>();
        // "sources" / "why?": what the last answer drew on (B1).
        services.AddSingleton<DiscordLastTurns>();
        // Exact text commands, tried in this order before anything else (A10, B1, H7).
        services.AddSingleton<IDiscordCommand, DiscordPauseCommand>();
        services.AddSingleton<IDiscordCommand, DiscordSources>();
        services.AddSingleton<IDiscordCommand, DiscordUpcoming>();
        // ADR-0037: INR readings and dose changes on this host; interactors noticed on the side.
        services.AddSingleton<IDiscordCommand, DiscordAnticoag>();
        // "ask …": Steve's own records, answered locally with sources (#3).
        services.AddSingleton<IDiscordCommand, DiscordAsk>();
        services.AddSingleton<DiscordInteractionWatch>();
        services.AddSingleton<DiscordReplyStreamer>();
        services.AddSingleton<DiscordImageResponder>();
        services.AddSingleton<DiscordTypingIndicator>();
        services.AddSingleton<DiscordAnswerer>();
        // A job drafted here comes back here (ADR-0030, migration 039).
        services.AddSingleton<Dami.Core.Scheduling.IScheduledPromptDelivery, DiscordScheduledDelivery>();
    }

    private static void AddTransport(IServiceCollection services, DiscordOptions options)
    {
        services.AddHttpClient(nameof(DiscordRest));
        services.AddSingleton<IDiscordRest>(provider => new DiscordRest(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(DiscordRest)),
            options.Token,
            provider.GetRequiredService<ILogger<DiscordRest>>()));

        services.AddSingleton<DiscordEgressChannel>(provider => new DiscordEgressChannel(
            static () => new DiscordSocket(),
            provider.GetRequiredService<IDiscordRest>(),
            options,
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<DiscordEgressChannel>>()));
        services.AddSingleton<IEgressChannel>(provider =>
            provider.GetRequiredService<DiscordEgressChannel>());
        services.AddSingleton<IProgressiveEgressChannel>(provider =>
            provider.GetRequiredService<DiscordEgressChannel>());
    }

    private static DiscordOptions Read(IConfiguration configuration)
    {
        var section = configuration.GetSection(DiscordOptions.SECTION);
        return new DiscordOptions
        {
            Token = section["Token"] ?? string.Empty,
            OwnerUserId = section["OwnerUserId"] ?? string.Empty,
            GuildId = section["GuildId"] ?? string.Empty,
            Enabled = bool.TryParse(section["Enabled"], out var enabled) && enabled,
            HistoryTurns = int.TryParse(section["HistoryTurns"], out var history) && history > 0
                ? history
                : 6,
            VisionTimeout = double.TryParse(
                section["VisionTimeoutSeconds"], out var visionSeconds) && visionSeconds > 0
                ? TimeSpan.FromSeconds(visionSeconds)
                : TimeSpan.FromSeconds(30),
            TypingRefresh = double.TryParse(
                section["TypingRefreshSeconds"], out var typingSeconds) && typingSeconds > 0
                ? TimeSpan.FromSeconds(typingSeconds)
                : TimeSpan.FromSeconds(8),
            CheckInConversationId = section["CheckInConversationId"] ?? string.Empty,
            CheckInHour = int.TryParse(section["CheckInHour"], out var hour) && hour is >= 0 and <= 23 ? hour : 9,
            CheckInTimeZone = section["CheckInTimeZone"] is { Length: > 0 } zone ? zone : "America/Chicago",
            CheckInVoice = !bool.TryParse(section["CheckInVoice"], out var voice) || voice,
            CheckInExcludedServices = section["CheckInExcludedServices"] is { } excluded
                ? excluded.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : ["repo-hygiene"],
        };
    }
}
