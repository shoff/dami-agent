using System.Net;
using System.Text;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Privacy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Privacy.Tests;

public sealed class McpEgressHttpMessageHandlerTests
{
    [Fact]
    public async Task SendAsync_Should_Send_An_Egressable_Post_And_Meter_It_Durably()
    {
        var network = new RecordingHandler();
        var events = Substitute.For<IExecutionEventStore>();
        var contexts = new AmbientEgressOperationContext();
        var options = new EgressOptions();
        options.AllowedHosts.Add("mcp.example");
        using var client = CreateClient(network, contexts, events, options);
        EgressOperationContext context = CreateContext();
        using IDisposable scope = contexts.Begin(context);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://mcp.example/mcp")
        {
            Content = new StringContent("{\"jsonrpc\":\"2.0\"}", Encoding.UTF8, "application/json"),
        };

        using HttpResponseMessage response = await client.SendAsync(request);

        var recordedEvents = events.ReceivedCalls()
            .Select(call => call.GetArguments()[0]).OfType<ExecutionEvent>().ToArray();
        Assert.Equal(
            (HttpMethod.Post, "{\"jsonrpc\":\"2.0\"}", HttpStatusCode.OK,
                "EgressRequested,EgressCompleted", true, 1, false),
            (network.Method, network.Body, response.StatusCode,
                string.Join(',', recordedEvents.Select(item => item.Type)),
                recordedEvents.All(item =>
                    item.TraceId == context.TraceId
                    && item.ParentSpanId == context.ParentSpanId
                    && item.Origin == context.Origin),
                recordedEvents.Select(item => item.SpanId).Distinct().Count(),
                recordedEvents.Any(item =>
                    item.Label.Contains("jsonrpc", StringComparison.OrdinalIgnoreCase))));
    }

    [Fact]
    public async Task SendAsync_Should_Refuse_A_Forbidden_Fragment_Before_Network_Io()
    {
        var network = new RecordingHandler();
        var events = Substitute.For<IExecutionEventStore>();
        var contexts = new AmbientEgressOperationContext();
        var options = new EgressOptions();
        options.AllowedHosts.Add("mcp.example");
        options.ForbiddenFragments.Add("steve hoff");
        using var client = CreateClient(network, contexts, events, options);
        using IDisposable scope = contexts.Begin(CreateContext());

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => client.GetAsync("https://mcp.example/tools?owner=steve%20hoff"));

        Assert.Equal(
            (0, ExecutionEventType.EgressRefused),
            (network.CallCount, LastEventType(events)));
    }

    [Fact]
    public async Task SendAsync_Should_Refuse_A_Request_Body_Above_The_Configured_Limit()
    {
        var network = new RecordingHandler();
        var events = Substitute.For<IExecutionEventStore>();
        var contexts = new AmbientEgressOperationContext();
        var options = new EgressOptions { MaxRequestBytes = 4 };
        options.AllowedHosts.Add("mcp.example");
        using var client = CreateClient(network, contexts, events, options);
        using IDisposable scope = contexts.Begin(CreateContext());
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://mcp.example/mcp")
        {
            Content = new StringContent("12345", Encoding.UTF8, "application/json"),
        };

        await Assert.ThrowsAsync<EgressRefusedException>(() => client.SendAsync(request));

        Assert.Equal(
            (0, ExecutionEventType.EgressRefused),
            (network.CallCount, LastEventType(events)));
    }

    [Fact]
    public async Task SendAsync_Should_Reject_A_Response_Above_The_Configured_Limit()
    {
        var network = new RecordingHandler("four");
        var events = Substitute.For<IExecutionEventStore>();
        var contexts = new AmbientEgressOperationContext();
        var options = new EgressOptions { MaxResponseBytes = 3 };
        options.AllowedHosts.Add("mcp.example");
        using var client = CreateClient(network, contexts, events, options);
        using IDisposable scope = contexts.Begin(CreateContext());

        await Assert.ThrowsAsync<InvalidDataException>(
            () => client.GetAsync("https://mcp.example/mcp"));
    }

    [Fact]
    public async Task SendAsync_Should_Refuse_A_CrossOrigin_Redirect()
    {
        var network = new RedirectHandler(new Uri("https://other.example/mcp"));
        var events = Substitute.For<IExecutionEventStore>();
        var contexts = new AmbientEgressOperationContext();
        var options = new EgressOptions();
        options.AllowedHosts.Add("mcp.example");
        options.AllowedHosts.Add("other.example");
        using var client = CreateClient(network, contexts, events, options);
        using IDisposable scope = contexts.Begin(CreateContext());

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => client.PostAsync("https://mcp.example/mcp", new StringContent("{}")));

        Assert.Equal(
            (1, ExecutionEventType.EgressRefused),
            (network.CallCount, LastEventType(events)));
    }

    [Fact]
    public async Task SendAsync_Should_Record_A_Network_Failure()
    {
        var events = Substitute.For<IExecutionEventStore>();
        var contexts = new AmbientEgressOperationContext();
        var options = new EgressOptions();
        options.AllowedHosts.Add("mcp.example");
        using var client = CreateClient(new FailingHandler(), contexts, events, options);
        using IDisposable scope = contexts.Begin(CreateContext());

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync("https://mcp.example/mcp"));

        Assert.Equal(ExecutionEventType.EgressFailed, LastEventType(events));
    }

    [Fact]
    public async Task SendAsync_Should_Refuse_LocalOnly_Content_Before_Network_Io()
    {
        var network = new RecordingHandler();
        var events = Substitute.For<IExecutionEventStore>();
        var contexts = new AmbientEgressOperationContext();
        var options = new EgressOptions();
        options.AllowedHosts.Add("mcp.example");
        using var client = CreateClient(network, contexts, events, options);
        using IDisposable scope = contexts.Begin(CreateContext(PrivacyClass.LocalOnly));

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => client.GetAsync("https://mcp.example/mcp"));

        Assert.Equal(
            (0, ExecutionEventType.EgressRefused),
            (network.CallCount, LastEventType(events)));
    }

    [Fact]
    public async Task SendAsync_Should_Fail_Closed_Without_An_Operation_Context()
    {
        var network = new RecordingHandler();
        var events = Substitute.For<IExecutionEventStore>();
        var contexts = new AmbientEgressOperationContext();
        var options = new EgressOptions();
        options.AllowedHosts.Add("mcp.example");
        using var client = CreateClient(network, contexts, events, options);

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => client.GetAsync("https://mcp.example/mcp"));

        Assert.Equal(0, network.CallCount);
    }

    [Fact]
    public async Task SendAsync_Should_Bound_A_Chunked_Response_While_It_Is_Read()
    {
        var network = new ContentHandler(new UnknownLengthContent("four"));
        var events = Substitute.For<IExecutionEventStore>();
        var contexts = new AmbientEgressOperationContext();
        var options = new EgressOptions { MaxResponseBytes = 3 };
        options.AllowedHosts.Add("mcp.example");
        using var client = CreateClient(network, contexts, events, options);
        using IDisposable scope = contexts.Begin(CreateContext());

        await Assert.ThrowsAsync<InvalidDataException>(
            () => client.GetAsync("https://mcp.example/mcp"));
    }

    private static HttpClient CreateClient(
        HttpMessageHandler network,
        AmbientEgressOperationContext contexts,
        IExecutionEventStore events,
        EgressOptions options)
    {
        var budget = Substitute.For<IEgressBudget>();
        budget.FindRefusalAsync(Arg.Any<CancellationToken>()).Returns((string?)null);
        return new HttpClient(new McpEgressHttpMessageHandler(
            network, contexts, budget, Options.Create(options), events,
            new FakeTimeProvider(DateTimeOffset.UnixEpoch),
            NullLogger<McpEgressHttpMessageHandler>.Instance));
    }

    private static EgressOperationContext CreateContext(
        PrivacyClass privacy = PrivacyClass.Egressable)
    {
        return new EgressOperationContext(
            "invoke MCP capability", privacy,
            Guid.NewGuid(), Guid.NewGuid(), ExecutionOrigin.UserTurn);
    }

    private static ExecutionEventType LastEventType(IExecutionEventStore events)
    {
        return events.ReceivedCalls()
            .Select(call => call.GetArguments()[0]).OfType<ExecutionEvent>().Last().Type;
    }

    private sealed class RecordingHandler(string responseBody = "{}") : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public HttpMethod? Method { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            this.CallCount++;
            this.Method = request.Method;
            this.Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody),
            };
        }
    }

    private sealed class RedirectHandler(Uri destination) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            this.CallCount++;
            var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            response.Headers.Location = destination;
            return Task.FromResult(response);
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("simulated network failure");
        }
    }

    private sealed class ContentHandler(HttpContent content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content,
            });
        }
    }

    private sealed class UnknownLengthContent(string value) : HttpContent
    {
        private readonly byte[] bytes = Encoding.UTF8.GetBytes(value);

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
        {
            await stream.WriteAsync(this.bytes).ConfigureAwait(false);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
