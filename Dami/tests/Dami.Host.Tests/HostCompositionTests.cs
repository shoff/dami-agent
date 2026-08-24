using System.Net;
using System.Text.Json;
using Dami.Capabilities;
using Dami.Capabilities.Mcp;
using Dami.Capabilities.Native;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Privacy;
using Dami.Privacy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Dami.Host.Tests;

public sealed class HostCompositionTests
{
    [Fact]
    public async Task Health_Should_Build_The_Production_Session_Composition()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Services_Should_Compose_One_Shared_Native_And_Mcp_Execution_Boundary()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/health", CancellationToken.None);

        var executor = factory.Services.GetRequiredService<ICapabilityExecutor>();
        ICapabilityExecutionSource[] sources = factory.Services
            .GetServices<ICapabilityExecutionSource>().ToArray();
        var scopeFactory = factory.Services.GetRequiredService<IEgressOperationScopeFactory>();
        var contextReader = factory.Services.GetRequiredService<IEgressOperationContextReader>();

        Assert.IsType<CapabilityExecutorDispatcher>(executor);
        Assert.Contains(sources, source => source is NativeCapabilityExecutor);
        Assert.Contains(sources, source => source is McpCapabilityExecutor);
        Assert.Same(scopeFactory, contextReader);
        Assert.IsAssignableFrom<IMcpEgressHttpHandler>(
            factory.Services.GetRequiredService<McpEgressHttpMessageHandler>());
    }

    [Fact]
    public async Task Host_Should_Discover_Invoke_And_Close_A_Local_Streamable_Http_Server()
    {
        await using var server = await FakeMcpHttpServer.StartAsync();
        var events = new List<ExecutionEvent>();
        IExecutionEventStore eventStore = CreateEventStore(events);
        WebApplicationFactory<Program> factory = CreateMcpFactory(server.Endpoint, eventStore);
        try
        {
            using var client = factory.CreateClient();
            using HttpResponseMessage health = await client.GetAsync("/health", CancellationToken.None);
            Assert.Single(factory.Services
                .GetRequiredService<IReadOnlyList<McpServerRegistration>>());
            CapabilityEntry capability = Assert.Single(
                factory.Services.GetRequiredService<ICapabilityInventory>()
                    .Snapshot(), entry => entry.Source == CapabilitySource.Mcp);

            CapabilityExecutionResult result = await InvokeAsync(factory.Services, capability);

            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            Assert.Equal("sunny in Austin", result.Output);
            Assert.Contains(events, item => item.Type == ExecutionEventType.TraceCompleted);
        }
        finally
        {
            await factory.DisposeAsync();
        }

        Assert.Equal(1, server.DiscoveryCount);
        Assert.Equal(1, server.InvocationCount);
        Assert.Equal(1, server.ShutdownCount);
        Assert.Equal(2, events.Count(item => item.Type == ExecutionEventType.TraceStarted));
        Assert.Equal(2, events.Count(item => item.Type == ExecutionEventType.TraceCompleted));
    }

    private static WebApplicationFactory<Program> CreateMcpFactory(
        Uri endpoint,
        IExecutionEventStore eventStore)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Mcp:Servers:0:ServerId"] = Guid.NewGuid().ToString("D"),
            ["Mcp:Servers:0:Name"] = "weather",
            ["Mcp:Servers:0:Endpoint"] = endpoint.AbsoluteUri,
            ["Mcp:Servers:0:Transport"] = "StreamableHttp",
            ["Mcp:Servers:0:Trust"] = "Trusted",
        };

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            foreach (KeyValuePair<string, string?> setting in settings)
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.ConfigureTestServices(services => services.AddSingleton(eventStore));
        });
    }

    private static Task<CapabilityExecutionResult> InvokeAsync(
        IServiceProvider services,
        CapabilityEntry capability)
    {
        JsonElement arguments = JsonSerializer.SerializeToElement(new { city = "Austin" });
        var request = new CapabilityExecutionRequest(
            Guid.NewGuid(), Guid.NewGuid(), PrivacyClass.Egressable, ExecutionOrigin.UserTurn,
            new CapabilityInvocation(capability.CapabilityId, arguments));
        return services.GetRequiredService<ICapabilityExecutor>()
            .ExecuteAsync(request, CancellationToken.None);
    }

    private static IExecutionEventStore CreateEventStore(ICollection<ExecutionEvent> events)
    {
        var eventStore = Substitute.For<IExecutionEventStore>();
        eventStore.AppendAsync(
                Arg.Do<ExecutionEvent>(events.Add), Arg.Any<CancellationToken>())
            .Returns(1L);
        return eventStore;
    }
}
