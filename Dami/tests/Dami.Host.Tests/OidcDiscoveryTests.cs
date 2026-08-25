using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace Dami.Host.Tests;

public sealed class OidcDiscoveryTests
{
    [Fact]
    public async Task Discovery_Should_Advertise_Only_The_Configured_Secure_Flows_Async()
    {
        await using WebApplicationFactory<Program> factory =
            new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("Authentication:Enabled", "true");
                builder.UseSetting("Authentication:AllowInsecureLoopback", "true");
                builder.UseSetting("Authentication:UseEphemeralKeys", "true");
                builder.UseSetting("Authentication:Issuer", "http://localhost");
            });
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            "/.well-known/openid-configuration", CancellationToken.None);
        using JsonDocument document = (await response.Content
            .ReadFromJsonAsync<JsonDocument>(CancellationToken.None))!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement root = document.RootElement;
        Assert.EndsWith("/connect/authorize", root.GetProperty("authorization_endpoint").GetString());
        Assert.EndsWith("/connect/token", root.GetProperty("token_endpoint").GetString());
        Assert.EndsWith(
            "/connect/device", root.GetProperty("device_authorization_endpoint").GetString());
        Assert.Contains("S256", root.GetProperty("code_challenge_methods_supported")
            .EnumerateArray().Select(item => item.GetString()!));
        Assert.Equal(
            ["authorization_code", "refresh_token", "urn:ietf:params:oauth:grant-type:device_code"],
            root.GetProperty("grant_types_supported").EnumerateArray()
                .Select(item => item.GetString()!).Order(StringComparer.Ordinal));
    }
}
