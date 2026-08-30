using Dami.Contracts.Privacy;
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

        services.AddHttpClient(nameof(DiscordRest));
        services.AddSingleton<IDiscordRest>(provider => new DiscordRest(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(DiscordRest)),
            options.Token,
            provider.GetRequiredService<ILogger<DiscordRest>>()));

        services.AddSingleton<IEgressChannel>(provider => new DiscordEgressChannel(
            static () => new DiscordSocket(),
            provider.GetRequiredService<IDiscordRest>(),
            options,
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<DiscordEgressChannel>>()));

        services.AddHostedService<DiscordGatewayWorker>();
        return services;
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
        };
    }
}
