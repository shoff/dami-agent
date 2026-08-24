using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dami.Host.Tests;

internal sealed class FakeMcpHttpServer : IAsyncDisposable
{
    private const string SESSION_ID = "dami-test-session";

    private readonly WebApplication application;
    private int discoveryCount;
    private int invocationCount;
    private int shutdownCount;

    private FakeMcpHttpServer(WebApplication application, Uri endpoint)
    {
        this.application = application;
        this.Endpoint = endpoint;
    }

    public Uri Endpoint { get; }

    public int DiscoveryCount => Volatile.Read(ref this.discoveryCount);

    public int InvocationCount => Volatile.Read(ref this.invocationCount);

    public int ShutdownCount => Volatile.Read(ref this.shutdownCount);

    public static async Task<FakeMcpHttpServer> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        WebApplication application = builder.Build();
        FakeMcpHttpServer? server = null;
        application.MapPost(
            "/mcp", (Func<HttpRequest, Task<IResult>>)(request => server!.HandlePostAsync(request)));
        application.MapDelete(
            "/mcp", (Func<HttpRequest, IResult>)(request => server!.HandleDelete(request)));
        await application.StartAsync().ConfigureAwait(false);
        Uri endpoint = FindEndpoint(application);
        server = new FakeMcpHttpServer(application, endpoint);
        return server;
    }

    public async ValueTask DisposeAsync()
    {
        await this.application.StopAsync().ConfigureAwait(false);
        await this.application.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<IResult> HandlePostAsync(HttpRequest request)
    {
        using JsonDocument document = await JsonDocument.ParseAsync(
            request.Body, cancellationToken: request.HttpContext.RequestAborted).ConfigureAwait(false);
        JsonElement root = document.RootElement;
        string method = root.GetProperty("method").GetString()
            ?? throw new InvalidDataException("MCP request method is missing.");
        if (!root.TryGetProperty("id", out JsonElement id))
        {
            return Results.Accepted();
        }

        object result = method switch
        {
            "initialize" => Initialize(request),
            "tools/list" => this.ListTools(request),
            "tools/call" => this.CallTool(request, root),
            _ => throw new InvalidDataException($"Unexpected MCP method '{method}'."),
        };
        return Results.Json(new { jsonrpc = "2.0", id = id.Clone(), result });
    }

    private IResult HandleDelete(HttpRequest request)
    {
        EnsureSession(request);
        Interlocked.Increment(ref this.shutdownCount);
        return Results.Ok();
    }

    private static object Initialize(HttpRequest request)
    {
        request.HttpContext.Response.Headers["Mcp-Session-Id"] = SESSION_ID;
        return new
        {
            protocolVersion = "2025-11-25",
            capabilities = new { tools = new { } },
            serverInfo = new { name = "Dami test MCP", version = "1.0" },
        };
    }

    private object ListTools(HttpRequest request)
    {
        EnsureSession(request);
        Interlocked.Increment(ref this.discoveryCount);
        return new
        {
            tools = new[]
            {
                new
                {
                    name = "weather",
                    description = "Look up the current weather.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new { city = new { type = "string" } },
                        required = new[] { "city" },
                    },
                },
            },
        };
    }

    private object CallTool(HttpRequest request, JsonElement root)
    {
        EnsureSession(request);
        string city = root.GetProperty("params").GetProperty("arguments")
            .GetProperty("city").GetString() ?? string.Empty;
        Interlocked.Increment(ref this.invocationCount);
        return new
        {
            content = new[] { new { type = "text", text = $"sunny in {city}" } },
            isError = false,
        };
    }

    private static void EnsureSession(HttpRequest request)
    {
        if (!string.Equals(request.Headers["Mcp-Session-Id"], SESSION_ID, StringComparison.Ordinal))
        {
            throw new InvalidDataException("MCP session identifier was not preserved.");
        }
    }

    private static Uri FindEndpoint(WebApplication application)
    {
        IServer server = application.Services.GetRequiredService<IServer>();
        IServerAddressesFeature addresses = server.Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Kestrel did not expose its bound address.");
        string address = Assert.Single(addresses.Addresses);
        return new Uri(new Uri(address), "/mcp");
    }
}
