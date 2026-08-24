using Dami.Capabilities;
using Dami.Capabilities.Sandboxed;
using Dami.Contracts.Capabilities;

namespace Dami.Host;

/// <summary>Composes approved sandboxed tools and startup recovery.</summary>
public static class SandboxedToolServiceCollectionExtensions
{
    /// <summary>Adds sandbox execution and recovery when a private runtime root is configured.</summary>
    public static IServiceCollection AddDamiSandboxedTools(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        SandboxedToolHostOptions options = Bind(configuration);
        if (string.IsNullOrWhiteSpace(options.RootDirectory))
        {
            return services;
        }

        Validate(options);
        services.AddSingleton(options);
        RegisterProcesses(services, options);
        RegisterActivation(services, options);
        services.AddSingleton<IHostedService, SandboxedToolRecoveryHostedService>();
        return services;
    }

    private static SandboxedToolHostOptions Bind(IConfiguration configuration)
    {
        var options = new SandboxedToolHostOptions();
        configuration.GetSection(SandboxedToolHostOptions.SECTION_NAME).Bind(options);
        return options;
    }

    private static void RegisterProcesses(
        IServiceCollection services,
        SandboxedToolHostOptions hostOptions)
    {
        SandboxProcessOptions verification = VerificationOptions(hostOptions);
        SandboxProcessOptions runtime = RuntimeOptions(hostOptions);
        services.AddSingleton<ToolEnvelopeWriter>();
        services.AddSingleton(provider => new ToolArtifactVerifier(
            provider.GetRequiredService<ToolEnvelopeWriter>(), CreateRunner(verification)));
        services.AddSingleton<ISandboxProcessRunner>(CreateRunner(runtime));
    }

    private static void RegisterActivation(
        IServiceCollection services,
        SandboxedToolHostOptions options)
    {
        services.AddSingleton<SandboxedCapabilityRegistry>();
        services.AddSingleton<ISandboxedCapabilityCatalog>(provider =>
            provider.GetRequiredService<SandboxedCapabilityRegistry>());
        services.AddSingleton<IRevertibleRegistrar<SandboxedCapabilityRegistration>>(provider =>
            provider.GetRequiredService<SandboxedCapabilityRegistry>());
        services.AddSingleton<SandboxedCapabilityPublisher>();
        services.AddSingleton<ISandboxedToolMaterializer>(provider =>
            new SandboxedToolMaterializer(
                options.RootDirectory!, provider.GetRequiredService<ToolArtifactVerifier>()));
        services.AddSingleton<ISandboxedToolActivator, SandboxedToolActivator>();
        services.AddSingleton<SandboxedToolRecoveryProcessor>();
        services.AddSingleton<SandboxedCapabilityExecutor>();
        services.AddSingleton<ICapabilityExecutionSource>(provider =>
            provider.GetRequiredService<SandboxedCapabilityExecutor>());
    }

    private static SandboxProcessRunner CreateRunner(SandboxProcessOptions options)
    {
        return new SandboxProcessRunner(new BubblewrapCommandFactory(options), options);
    }

    private static SandboxProcessOptions VerificationOptions(SandboxedToolHostOptions options)
    {
        return new SandboxProcessOptions
        {
            MaxOutputBytes = 1_048_576,
            MemoryMaxBytes = 2_147_483_648,
            ProcessMax = 128,
            RuntimeMax = TimeSpan.FromSeconds(60),
            UserRuntimeDirectory = options.UserRuntimeDirectory,
        };
    }

    private static SandboxProcessOptions RuntimeOptions(SandboxedToolHostOptions options)
    {
        return new SandboxProcessOptions
        {
            UserRuntimeDirectory = options.UserRuntimeDirectory,
        };
    }

    private static void Validate(SandboxedToolHostOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RootDirectory)
            || !Path.IsPathFullyQualified(options.RootDirectory)
            || options.RecoveryBatchSize is <= 0 or > 1_000
            || !Path.IsPathFullyQualified(options.UserRuntimeDirectory))
        {
            throw new InvalidOperationException(
                "SandboxedTools requires absolute roots and a recovery batch of 1–1000.");
        }

        if (!Directory.Exists(options.RootDirectory)
            || (File.GetAttributes(options.RootDirectory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                "SandboxedTools requires a provisioned ordinary runtime root.");
        }
    }

    private sealed class SandboxedToolRecoveryHostedService(
        SandboxedToolRecoveryProcessor processor,
        SandboxedToolHostOptions options,
        ILogger<SandboxedToolRecoveryHostedService> logger) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            ToolActivationRecoverySummary summary = await processor.RecoverAsync(
                options.RecoveryBatchSize, cancellationToken).ConfigureAwait(false);
            if (summary.Failed > 0)
            {
                throw new InvalidOperationException(
                    $"Sandboxed tool recovery failed for {summary.Failed} durable activation(s).");
            }

            logger.LogInformation(
                "Sandboxed tool recovery completed: {SucceededCount}/{FoundCount}",
                summary.Succeeded,
                summary.Found);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
