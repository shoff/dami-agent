using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Dami.Host.Tests;

public sealed class TaskBoardDashboardTests
{
    [Fact]
    public async Task Index_Should_Expose_The_Live_Recursive_Task_Board()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/", CancellationToken.None);
        var html = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"taskboards\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"boardlist\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"tasktree\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"boardactivity\"", html, StringComparison.Ordinal);
        Assert.Contains("/task-boards", html, StringComparison.Ordinal);
        Assert.Contains("renderBoardTask", html, StringComparison.Ordinal);
    }
}
