using Dami.Capabilities;
using Dami.Capabilities.Skills;
using Dami.Contracts.Capabilities;
using Dami.Core.Turns;

namespace Dami.Host;

/// <summary>Composes optional filesystem skills and prompt disclosure.</summary>
public static class SkillServiceCollectionExtensions
{
    /// <summary>Adds bounded skill loading and progressive prompt disclosure.</summary>
    public static IServiceCollection AddDamiSkills(
        this IServiceCollection services,
        IConfiguration configuration,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(clock);
        services.Configure<SkillPromptOptions>(
            configuration.GetSection(SkillPromptOptions.SECTION_NAME));
        services.AddSingleton<ISkillPromptBuilder, SkillPromptBuilder>();

        var options = new SkillLoaderOptions();
        configuration.GetSection(SkillLoaderOptions.SECTION_NAME).Bind(options);
        if (string.IsNullOrWhiteSpace(options.RootDirectory))
        {
            services.AddSingleton<ISkillContentReader, UnavailableSkillContentReader>();
            return services;
        }

        services.AddSingleton(options);
        services.AddSingleton<SkillCapabilityLoader>(provider => new SkillCapabilityLoader(
            provider.GetRequiredService<ICapabilitySourceSnapshotRegistrar>(), options));
        services.AddSingleton<ISkillContentReader>(provider =>
            provider.GetRequiredService<SkillCapabilityLoader>());
        services.AddSingleton<IHostedService>(provider => new SkillLoaderHostedService(
            provider.GetRequiredService<SkillCapabilityLoader>(), clock));
        return services;
    }

    private sealed class SkillLoaderHostedService(
        SkillCapabilityLoader loader,
        TimeProvider clock) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            return loader.LoadAsync(clock.GetUtcNow(), cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class UnavailableSkillContentReader : ISkillContentReader
    {
        public Task<string> ReadBodyAsync(
            Guid skillId,
            string expectedVersion,
            CancellationToken cancellationToken)
        {
            return Task.FromException<string>(Unavailable());
        }

        public Task<string> ReadReferenceAsync(
            Guid skillId,
            string expectedVersion,
            string relativePath,
            CancellationToken cancellationToken)
        {
            return Task.FromException<string>(Unavailable());
        }

        private static InvalidOperationException Unavailable()
        {
            return new InvalidOperationException(
                "Skill content is unavailable because no skill root is configured.");
        }
    }
}
