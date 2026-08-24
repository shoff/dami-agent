using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
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
}
