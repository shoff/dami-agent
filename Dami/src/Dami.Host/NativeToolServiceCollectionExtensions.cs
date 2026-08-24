using Dami.Capabilities;
using Dami.Capabilities.Native;
using Dami.Contracts.Approvals;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Models;
using Dami.Core.Turns;
using Dami.Providers;

namespace Dami.Host;

/// <summary>Composes configured native capabilities into the interactive tool runtime.</summary>
public static class NativeToolServiceCollectionExtensions
{
    /// <summary>Adds discovery, semantic selection, activation, execution, and the tool model.</summary>
    public static IServiceCollection AddDamiNativeTools(
        this IServiceCollection services,
        IConfiguration configuration,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(clock);

        var activeTypes = RegisterConfiguredHandlers(services, configuration);
        RegisterCapabilityCatalogs(services, activeTypes, clock);
        RegisterSelection(services, configuration);
        RegisterExecution(services, configuration);
        return services;
    }

    private static HashSet<Type> RegisterConfiguredHandlers(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var activeTypes = new HashSet<Type>();
        RegisterReadFile(services, configuration, activeTypes);
        RegisterRunProcess(services, configuration, activeTypes);
        RegisterFilePatch(services, configuration, activeTypes);
        return activeTypes;
    }

    private static void RegisterReadFile(
        IServiceCollection services,
        IConfiguration configuration,
        ISet<Type> activeTypes)
    {
        var options = Bind<ReadFileCapabilityOptions>(configuration, ReadFileCapabilityOptions.SECTION_NAME);
        if (string.IsNullOrWhiteSpace(options.RootDirectory))
        {
            return;
        }

        services.AddSingleton(options);
        services.AddSingleton<ReadFileCapabilityHandler>();
        activeTypes.Add(typeof(ReadFileCapabilityHandler));
    }

    private static void RegisterRunProcess(
        IServiceCollection services,
        IConfiguration configuration,
        ISet<Type> activeTypes)
    {
        var options = Bind<RunProcessCapabilityOptions>(
            configuration, RunProcessCapabilityOptions.SECTION_NAME);
        var hasRoot = !string.IsNullOrWhiteSpace(options.RootDirectory);
        var hasAllowlist = options.AllowedExecutables.Count > 0;
        if (!hasRoot && !hasAllowlist)
        {
            return;
        }

        if (!hasRoot || !hasAllowlist)
        {
            throw new InvalidOperationException(
                "RunProcess requires both RootDirectory and at least one AllowedExecutables entry.");
        }

        services.AddSingleton(options);
        services.AddSingleton<RunProcessCapabilityHandler>();
        activeTypes.Add(typeof(RunProcessCapabilityHandler));
    }

    private static void RegisterFilePatch(
        IServiceCollection services,
        IConfiguration configuration,
        ISet<Type> activeTypes)
    {
        var options = Bind<ProposeFilePatchCapabilityOptions>(
            configuration, ProposeFilePatchCapabilityOptions.SECTION_NAME);
        if (string.IsNullOrWhiteSpace(options.RootDirectory))
        {
            return;
        }

        services.AddSingleton(options);
        services.AddSingleton<ProposeFilePatchCapabilityHandler>();
        services.AddSingleton<FilePatchExecutor>();
        services.AddSingleton<IApprovalExecutionHandler>(provider =>
            provider.GetRequiredService<FilePatchExecutor>());
        activeTypes.Add(typeof(ProposeFilePatchCapabilityHandler));
    }

    private static void RegisterCapabilityCatalogs(
        IServiceCollection services,
        IReadOnlySet<Type> activeTypes,
        TimeProvider clock)
    {
        var capabilities = new CapabilityRegistry();
        var schemas = new CapabilityToolSchemaRegistry();
        var discovery = new NativeCapabilityDiscovery();
        var discovered = discovery.Discover(
            typeof(ReadFileCapabilityHandler).Assembly, clock.GetUtcNow());
        var active = discovered.Where(item => activeTypes.Contains(item.ImplementationType)).ToArray();
        new NativeCapabilityLoader(discovery, capabilities, schemas).Publish(active);

        services.AddSingleton(capabilities);
        services.AddSingleton<ICapabilityCatalog>(capabilities);
        services.AddSingleton<ICapabilityInventory>(capabilities);
        services.AddSingleton<ICapabilityRegistrar>(capabilities);
        services.AddSingleton<ICapabilityToolSchemaCatalog>(schemas);
        services.AddSingleton<ICapabilityToolSchemaRegistrar>(schemas);
        services.AddSingleton<IReadOnlyList<NativeCapabilityRegistration>>(active);
    }

    private static void RegisterSelection(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var options = Bind<CapabilityRetrievalOptions>(
            configuration, CapabilityRetrievalOptions.SECTION_NAME);
        services.AddSingleton<ICapabilityBundleExpander, CapabilityBundleExpander>();
        services.AddSingleton<ICapabilityIndexSynchronizer, CapabilityIndexSynchronizer>();
        services.AddSingleton<ICapabilityResolver>(provider => new SemanticCapabilityResolver(
            provider.GetRequiredService<ICapabilityIndexSynchronizer>(),
            provider.GetRequiredService<IEmbeddingClient>(),
            provider.GetRequiredService<ICapabilityEmbeddingStore>(),
            provider.GetRequiredService<IRerankClient>(),
            provider.GetRequiredService<ICapabilityCatalog>(),
            provider.GetRequiredService<ICapabilityBundleExpander>(),
            options));
        services.AddSingleton<ICapabilityToolResolver, SemanticCapabilityToolResolver>();
    }

    private static void RegisterExecution(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var executorOptions = Bind<NativeCapabilityExecutorOptions>(
            configuration, NativeCapabilityExecutorOptions.SECTION_NAME);
        var loopOptions = Bind<ToolLoopOptions>(configuration, ToolLoopOptions.SECTION_NAME);
        services.AddSingleton<NativeCapabilityRegistry>();
        services.AddSingleton<INativeCapabilityCatalog>(provider =>
            ActivateNativeCatalog(provider));
        services.AddSingleton<NativeCapabilityExecutor>(provider => new NativeCapabilityExecutor(
            provider.GetRequiredService<INativeCapabilityCatalog>(), executorOptions));
        services.AddSingleton<ICapabilityExecutionSource>(provider =>
            provider.GetRequiredService<NativeCapabilityExecutor>());
        services.AddSingleton<IToolLoopRunner>(provider => new ToolLoopRunner(
            provider.GetRequiredService<IToolCallingChatClient>(),
            provider.GetRequiredService<ICapabilityExecutor>(),
            provider.GetRequiredService<Dami.Contracts.Events.IExecutionEventStore>(),
            provider.GetRequiredService<TimeProvider>(),
            loopOptions));
        services.AddHttpClient<IToolCallingChatClient, OllamaToolCallingChatClient>(client =>
            client.Timeout = TimeSpan.FromMinutes(10));
    }

    private static INativeCapabilityCatalog ActivateNativeCatalog(IServiceProvider provider)
    {
        var registry = provider.GetRequiredService<NativeCapabilityRegistry>();
        var registrations = provider.GetRequiredService<IReadOnlyList<NativeCapabilityRegistration>>();
        new NativeCapabilityActivator(registry).Activate(
            registrations,
            type => provider.GetService(type) as INativeCapabilityHandler);
        return registry;
    }

    private static TOptions Bind<TOptions>(IConfiguration configuration, string sectionName)
        where TOptions : new()
    {
        var options = new TOptions();
        configuration.GetSection(sectionName).Bind(options);
        return options;
    }
}
