using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dami.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Xunit;

namespace Dami.Host.Tests;

public sealed class OidcDiscoveryTests
{
    [Fact]
    public async Task Gui_Authorization_Code_With_Pkce_Should_Call_The_Runtime()
    {
        await using WebApplicationFactory<Program> factory = CreateAuthenticatedFactory();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<DamiIdentity>>();
        Uri redirect = new("http://127.0.0.1:5812/callback");
        OpenIddictApplicationDescriptor descriptor = DamiClientProfiles.Gui(redirect);
        descriptor.ClientId = $"host-gui-{Guid.NewGuid():N}";
        object application = await applications.CreateAsync(descriptor, CancellationToken.None);
        var user = new DamiIdentity { UserName = $"gui-{Guid.NewGuid():N}" };
        Assert.True((await users.CreateAsync(user, "Gui-test-password-42!")).Succeeded);
        try
        {
            string verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
            string code = await AuthorizeCodeAsync(client, descriptor.ClientId, redirect,
                verifier, user.UserName!, "Gui-test-password-42!");
            string token = await ExchangeCodeAsync(
                client, descriptor.ClientId, redirect, verifier, code);
            client.DefaultRequestHeaders.Authorization = new("Bearer", token);
            Assert.Equal(HttpStatusCode.OK,
                (await client.GetAsync("/task-boards?limit=1")).StatusCode);
        }
        finally
        {
            await users.DeleteAsync(user);
            await applications.DeleteAsync(application, CancellationToken.None);
        }
    }

    private static async Task<string> AuthorizeCodeAsync(
        HttpClient client, string clientId, Uri redirect, string verifier,
        string username, string password)
    {
        string challenge = WebEncoders.Base64UrlEncode(
            SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        using HttpResponseMessage response = await client.PostAsync("/connect/authorize",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId, ["redirect_uri"] = redirect.AbsoluteUri,
                ["response_type"] = "code", ["scope"] = DamiAuthorizationScopes.RUNTIME_READ,
                ["code_challenge"] = challenge, ["code_challenge_method"] = "S256",
                ["username"] = username, ["password"] = password,
            }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var query = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
        return query["code"]!;
    }

    private static async Task<string> ExchangeCodeAsync(
        HttpClient client, string clientId, Uri redirect, string verifier, string code)
    {
        using HttpResponseMessage response = await client.PostAsync("/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId, ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirect.AbsoluteUri, ["code"] = code,
                ["code_verifier"] = verifier,
            }));
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        using var payload = JsonDocument.Parse(body);
        return payload.RootElement.GetProperty("access_token").GetString()!;
    }

    [Fact]
    public async Task Cli_Device_Flow_Should_Verify_A_User_And_Call_The_Runtime()
    {
        await using WebApplicationFactory<Program> factory = CreateAuthenticatedFactory();
        using HttpClient client = factory.CreateClient();
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<DamiIdentity>>();
        OpenIddictApplicationDescriptor descriptor = DamiClientProfiles.Cli();
        descriptor.ClientId = $"host-cli-{Guid.NewGuid():N}";
        object application = await applications.CreateAsync(descriptor, CancellationToken.None);
        var user = new DamiIdentity { UserName = $"device-{Guid.NewGuid():N}" };
        Assert.True((await users.CreateAsync(user, "Device-test-password-42!")).Succeeded);
        try
        {
            DeviceAuthorization device = await RequestDeviceAsync(client, descriptor.ClientId);
            using HttpResponseMessage verified = await VerifyDeviceAsync(
                client, device.UserCode, user.UserName!, "Device-test-password-42!");
            (HttpStatusCode status, string? token, string error) = await ExchangeDeviceAsync(
                client, descriptor.ClientId, device.DeviceCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage runtime = await client.GetAsync("/task-boards?limit=1");

            Assert.True(verified.IsSuccessStatusCode, await verified.Content.ReadAsStringAsync());
            Assert.True(status == HttpStatusCode.OK, error);
            Assert.Equal(HttpStatusCode.OK, runtime.StatusCode);
        }
        finally
        {
            Assert.True((await users.DeleteAsync(user)).Succeeded);
            await applications.DeleteAsync(application, CancellationToken.None);
        }
    }

    [Fact]
    public async Task Cli_Profile_Should_Receive_A_Device_And_User_Code()
    {
        await using WebApplicationFactory<Program> factory = CreateAuthenticatedFactory();
        using HttpClient client = factory.CreateClient();
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        OpenIddictApplicationDescriptor descriptor = DamiClientProfiles.Cli();
        descriptor.ClientId = $"host-cli-{Guid.NewGuid():N}";
        object application = await applications.CreateAsync(descriptor, CancellationToken.None);
        try
        {
            using HttpResponseMessage response = await client.PostAsync("/connect/device",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = descriptor.ClientId,
                    ["scope"] = DamiAuthorizationScopes.RUNTIME_READ,
                }), CancellationToken.None);
            using JsonDocument payload = (await response.Content.ReadFromJsonAsync<JsonDocument>())!;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(string.IsNullOrWhiteSpace(
                payload.RootElement.GetProperty("device_code").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(
                payload.RootElement.GetProperty("user_code").GetString()));
        }
        finally
        {
            await applications.DeleteAsync(application, CancellationToken.None);
        }
    }

    [Fact]
    public async Task Read_Only_Service_Should_Enroll_Exchange_And_Call_The_Runtime()
    {
        await using WebApplicationFactory<Program> factory = CreateAuthenticatedFactory();
        using HttpClient client = factory.CreateClient();
        string clientId = $"host-service-{Guid.NewGuid():N}";
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<DamiClientProvisioner>();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        object? application = null;
        try
        {
            string secret = await provisioner.EnrollServiceAsync(
                clientId, "Host test service", [DamiAuthorizationScopes.RUNTIME_READ],
                CancellationToken.None);
            application = await applications.FindByClientIdAsync(clientId, CancellationToken.None);
            (HttpStatusCode tokenStatus, string? accessToken, string error) = await ExchangeAsync(
                client, clientId, secret);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", accessToken);
            using HttpResponseMessage runtime = await client.GetAsync(
                "/task-boards?limit=1", CancellationToken.None);

            Assert.True(tokenStatus == HttpStatusCode.OK, error);
            Assert.Equal(HttpStatusCode.OK, runtime.StatusCode);
        }
        finally
        {
            if (application is not null)
            {
                await applications.DeleteAsync(application, CancellationToken.None);
            }
        }
    }

    private static async Task<(HttpStatusCode Status, string? AccessToken, string Error)> ExchangeAsync(
        HttpClient client, string clientId, string secret)
    {
        using HttpResponseMessage response = await client.PostAsync("/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId, ["client_secret"] = secret,
                ["grant_type"] = "client_credentials",
                ["scope"] = DamiAuthorizationScopes.RUNTIME_READ,
            }), CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var payload = JsonDocument.Parse(body);
        return (response.StatusCode, payload.RootElement.TryGetProperty(
            "access_token", out JsonElement token) ? token.GetString() : null, body);
    }

    private static async Task<DeviceAuthorization> RequestDeviceAsync(
        HttpClient client, string clientId)
    {
        using HttpResponseMessage response = await client.PostAsync("/connect/device",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["scope"] = DamiAuthorizationScopes.RUNTIME_READ,
            }));
        using JsonDocument payload = (await response.Content.ReadFromJsonAsync<JsonDocument>())!;
        return new DeviceAuthorization(
            payload.RootElement.GetProperty("device_code").GetString()!,
            payload.RootElement.GetProperty("user_code").GetString()!);
    }

    private static Task<HttpResponseMessage> VerifyDeviceAsync(
        HttpClient client, string userCode, string username, string password) =>
        client.PostAsync("/connect/verify", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["user_code"] = userCode, ["username"] = username, ["password"] = password,
            }));

    private static async Task<(HttpStatusCode Status, string? AccessToken, string Error)>
        ExchangeDeviceAsync(HttpClient client, string clientId, string deviceCode)
    {
        using HttpResponseMessage response = await client.PostAsync("/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["device_code"] = deviceCode,
            }));
        var body = await response.Content.ReadAsStringAsync();
        using var payload = JsonDocument.Parse(body);
        return (response.StatusCode, payload.RootElement.TryGetProperty(
            "access_token", out JsonElement token) ? token.GetString() : null, body);
    }

    [Fact]
    public async Task Runtime_Should_Reject_An_Invalid_Bearer_Token()
    {
        await using WebApplicationFactory<Program> factory = CreateAuthenticatedFactory();
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", "not-a-token");

        using HttpResponseMessage response = await client.GetAsync(
            "/task-boards", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_Should_Be_The_Only_Anonymous_Runtime_Route()
    {
        await using WebApplicationFactory<Program> factory = CreateAuthenticatedFactory();
        _ = factory.CreateClient();
        EndpointDataSource source = factory.Services.GetRequiredService<EndpointDataSource>();
        RouteEndpoint[] routes = source.Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(
                "/", StringComparison.Ordinal) == true)
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(
                "/connect/", StringComparison.Ordinal) == false)
            .ToArray();

        RouteEndpoint anonymous = Assert.Single(routes, endpoint =>
            endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null);

        Assert.Equal("/health", anonymous.RoutePattern.RawText);
    }

    [Fact]
    public async Task Approval_Resolution_Should_Require_The_Dedicated_Scope_Policy()
    {
        await using WebApplicationFactory<Program> factory = CreateAuthenticatedFactory();
        _ = factory.CreateClient();
        EndpointDataSource source = factory.Services.GetRequiredService<EndpointDataSource>();
        RouteEndpoint endpoint = Assert.Single(source.Endpoints.OfType<RouteEndpoint>(), item =>
            item.RoutePattern.RawText == "/approvals/{prefix}/resolve");

        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            item => item.Policy == DamiAuthorizationPolicies.APPROVALS_RESOLVE);
    }

    [Fact]
    public async Task Runtime_Should_Require_Authentication_While_Health_Remains_Anonymous()
    {
        await using WebApplicationFactory<Program> factory = CreateAuthenticatedFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage runtime = await client.GetAsync(
            "/task-boards", CancellationToken.None);
        using HttpResponseMessage health = await client.GetAsync(
            "/health", CancellationToken.None);

        Assert.True(
            runtime.StatusCode == HttpStatusCode.Unauthorized,
            await runtime.Content.ReadAsStringAsync(CancellationToken.None));
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task Discovery_Should_Advertise_Only_The_Configured_Secure_Flows_Async()
    {
        await using WebApplicationFactory<Program> factory = CreateAuthenticatedFactory();
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
            ["authorization_code", "client_credentials", "refresh_token",
                "urn:ietf:params:oauth:grant-type:device_code"],
            root.GetProperty("grant_types_supported").EnumerateArray()
                .Select(item => item.GetString()!).Order(StringComparer.Ordinal));
    }

    private static WebApplicationFactory<Program> CreateAuthenticatedFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Authentication:Enabled", "true");
            builder.UseSetting("Authentication:AllowInsecureLoopback", "true");
            builder.UseSetting("Authentication:UseEphemeralKeys", "true");
            builder.UseSetting("Authentication:Issuer", "http://localhost");
        });
    }

    private sealed record DeviceAuthorization(string DeviceCode, string UserCode);
}
