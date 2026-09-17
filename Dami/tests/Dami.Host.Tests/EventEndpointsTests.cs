using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dami.Contracts.Events;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Dami.Host.Tests;

public sealed class EventEndpointsTests
{
    [Fact]
    public async Task Recent_Events_Should_Return_The_Requested_Bounded_Window_Async()
    {
        var store = Substitute.For<IExecutionEventStore>();
        store.ReadRecentAsync(2, Arg.Any<CancellationToken>()).Returns(EventsAsync());
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton(store)));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/events/recent?limit=2", CancellationToken.None);
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([71L, 72L], body!.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("sequence").GetInt64()));
    }

    [Fact]
    public async Task Recent_Events_Should_Reject_An_Oversized_Window_Async()
    {
        var store = Substitute.For<IExecutionEventStore>();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton(store)));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/events/recent?limit=501", CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        _ = store.DidNotReceive().ReadRecentAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private static async IAsyncEnumerable<ExecutionEvent> EventsAsync()
    {
        foreach (var sequence in new[] { 71L, 72L })
        {
            yield return new ExecutionEvent(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, ExecutionOrigin.UserTurn,
                "runtime", ExecutionEventType.AgentProgressed, ExecutionStatus.Running,
                DateTimeOffset.UnixEpoch, "working", sequence: sequence);
        }

        await Task.CompletedTask;
    }
}
