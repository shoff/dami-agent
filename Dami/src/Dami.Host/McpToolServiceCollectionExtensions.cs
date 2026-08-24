using Dami.Capabilities;
using Dami.Capabilities.Mcp;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Events;
using Dami.Contracts.Privacy;
using Dami.Privacy;
using Microsoft.Extensions.Options;

namespace Dami.Host;

/// <summary>Composes MCP discovery, execution, and the dedicated egress boundary.</summary>
public static class McpToolServiceCollectionExtensions
{
    /// <summary>Adds the MCP source to the shared capability runtime.</summary>
    public static IServiceCollection AddDamiMcpTools(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        RegisterCatalogs(services);
        RegisterExecution(services, configuration);
        RegisterEgress(services, configuration);
        RegisterLifecycle(services, configuration);
        return services;
    }

    private static void RegisterCatalogs(IServiceCollection services)
    {
        services.AddSingleton<McpCapabilityRegistry>();
        services.AddSingleton<IMcpCapabilityCatalog>(provider =>
            provider.GetRequiredService<McpCapabilityRegistry>());
        services.AddSingleton<IMcpCapabilityRegistrar>(provider =>
            provider.GetRequiredService<McpCapabilityRegistry>());
        services.AddSingleton<IMcpDescriptionSummarizer, LocalMcpDescriptionSummarizer>();
        services.AddSingleton<McpCapabilityNormalizer>();
        services.AddSingleton<McpCapabilityLoader>();
    }

    private static void RegisterExecution(
        IServiceCollection services,
        IConfiguration configuration)
    {
        McpCapabilityExecutorOptions options = Bind<McpCapabilityExecutorOptions>(
            configuration, McpCapabilityExecutorOptions.SECTION_NAME);
        services.AddSingleton(options);
        services.AddSingleton<McpCapabilityExecutor>();
        services.AddSingleton<ICapabilityExecutionSource>(provider =>
            provider.GetRequiredService<McpCapabilityExecutor>());
        services.AddSingleton<ICapabilityExecutor>(provider =>
            new CapabilityExecutorDispatcher(
                provider.GetServices<ICapabilityExecutionSource>()));
    }

    private static void RegisterEgress(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<EgressOptions>(
            configuration.GetSection(EgressOptions.SECTION_NAME));
        services.AddSingleton<AmbientEgressOperationContext>();
        services.AddSingleton<IEgressOperationContextReader>(provider =>
            provider.GetRequiredService<AmbientEgressOperationContext>());
        services.AddSingleton<IEgressOperationScopeFactory>(provider =>
            provider.GetRequiredService<AmbientEgressOperationContext>());
        services.AddSingleton(provider => new McpEgressHttpMessageHandler(
            new SocketsHttpHandler { AllowAutoRedirect = false },
            provider.GetRequiredService<IEgressOperationContextReader>(),
            provider.GetRequiredService<IEgressBudget>(),
            provider.GetRequiredService<IOptions<EgressOptions>>(),
            provider.GetRequiredService<IExecutionEventStore>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<McpEgressHttpMessageHandler>>()));
    }

    private static void RegisterLifecycle(
        IServiceCollection services,
        IConfiguration configuration)
    {
        McpHostOptions options = Bind<McpHostOptions>(configuration, McpHostOptions.SECTION_NAME);
        IReadOnlyList<McpServerRegistration> registrations = Array.AsReadOnly(
            options.Servers.Select(server => server.ToRegistration()).ToArray());
        services.AddSingleton(registrations);
        services.AddSingleton<IHostedService, McpCapabilityHostedService>();
    }

    private static TOptions Bind<TOptions>(IConfiguration configuration, string sectionName)
        where TOptions : new()
    {
        var options = new TOptions();
        configuration.GetSection(sectionName).Bind(options);
        return options;
    }
}
