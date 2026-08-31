using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Dami.Host.Tests;

public sealed class FitnessEndpointTests
{
    [Fact]
    public async Task Fitness_Should_Serve_The_Whole_Domain_As_One_Snapshot()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/fitness", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken.None));
        Assert.Equal(
            (JsonValueKind.Array, JsonValueKind.Array, JsonValueKind.Array),
            (body.RootElement.GetProperty("cardio").ValueKind,
                body.RootElement.GetProperty("sets").ValueKind,
                body.RootElement.GetProperty("weighIns").ValueKind));
    }
}
