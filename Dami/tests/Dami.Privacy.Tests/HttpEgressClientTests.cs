using System.Net;
using Dami.Contracts.Events;
using Dami.Contracts.Privacy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Privacy.Tests;

/// <summary>The egress boundary: allowlist, tripwire, and the durable event trail.</summary>
public sealed class HttpEgressClientTests
{
    private readonly IEgressBudget egressBudget = Substitute.For<IEgressBudget>();

    public HttpEgressClientTests()
    {
        // NSubstitute's auto-stub for Task<string?> is "", which reads as a refusal.
        this.egressBudget.FindRefusalAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));
    }

    private static readonly DateTimeOffset now = new(2026, 8, 23, 3, 0, 0, TimeSpan.Zero);
    private static readonly Guid traceId = Guid.NewGuid();

    private readonly IExecutionEventStore eventStore = Substitute.For<IExecutionEventStore>();

    [Fact]
    public void Constructor_Should_Reject_A_Null_EventStore()
    {
        Assert.Throws<ArgumentNullException>(() => new HttpEgressClient(
            new HttpClient(new FakeHttpMessageHandler(HttpStatusCode.OK, "")),
            this.egressBudget,
            Options.Create(new EgressOptions()), null!, new FakeTimeProvider(now),
            NullLogger<HttpEgressClient>.Instance));
    }

    [Fact]
    public void Constructor_Should_Reject_A_Nonpositive_Response_Limit()
    {
        var options = Options.Create(new EgressOptions { MaxResponseBytes = 0 });

        Assert.Throws<ArgumentOutOfRangeException>("egressOptions", () => new HttpEgressClient(
            new HttpClient(new FakeHttpMessageHandler(HttpStatusCode.OK, "")),
            this.egressBudget,
            options,
            this.eventStore,
            new FakeTimeProvider(now),
            NullLogger<HttpEgressClient>.Instance));
    }

    [Fact]
    public async Task SendAsync_Should_Fetch_From_An_Allowlisted_Host()
    {
        var client = this.CreateClient(out _, allowed: "news.ycombinator.com", body: "the front page");

        var response = await client.SendAsync(Ask("https://news.ycombinator.com/rss"), CancellationToken.None);

        Assert.Equal("the front page", response.Body);
    }

    [Fact]
    public async Task SendAsync_Should_Refuse_A_Host_That_Is_Not_Allowlisted()
    {
        var client = this.CreateClient(out _, allowed: "news.ycombinator.com");

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => client.SendAsync(Ask("https://tracker.example.com/beacon"), CancellationToken.None));
    }

    [Fact]
    public async Task SendAsync_Should_Refuse_When_The_Budget_Is_Exhausted_Without_Touching_The_Network()
    {
        this.egressBudget.FindRefusalAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("Egress budget exhausted"));
        var client = this.CreateClient(out var handler, allowed: "news.ycombinator.com");

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => client.SendAsync(Ask("https://news.ycombinator.com/"), CancellationToken.None));

        Assert.Empty(handler.Sent);
    }

    [Fact]
    public async Task SendAsync_Should_Refuse_Everything_When_The_Allowlist_Is_Empty()
    {
        var client = this.CreateClient(out _);

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => client.SendAsync(Ask("https://example.com/"), CancellationToken.None));
    }

    [Fact]
    public async Task SendAsync_Should_Not_Reach_The_Network_When_Refused()
    {
        var client = this.CreateClient(out var handler, allowed: "news.ycombinator.com");

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => client.SendAsync(Ask("https://tracker.example.com/beacon"), CancellationToken.None));

        Assert.Empty(handler.Sent);
    }

    [Fact]
    public async Task SendAsync_Should_Trip_On_A_Forbidden_Fragment_In_The_Uri()
    {
        var client = this.CreateClient(
            out var handler, allowed: "www.youtube.com", forbidden: "hoff");

        await Assert.ThrowsAsync<EgressRefusedException>(() => client.SendAsync(
            Ask("https://www.youtube.com/results?search_query=steve+hoff+health"),
            CancellationToken.None));

        Assert.Empty(handler.Sent);
    }

    [Fact]
    public async Task SendAsync_Should_Record_An_EgressCompleted_Event_On_Success()
    {
        var client = this.CreateClient(out _, allowed: "news.ycombinator.com");

        await client.SendAsync(Ask("https://news.ycombinator.com/rss"), CancellationToken.None);

        await this.eventStore.Received(1).AppendAsync(
            Arg.Is<ExecutionEvent>(item =>
                item.Type == ExecutionEventType.EgressCompleted && item.TraceId == traceId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_Should_Record_An_EgressRefused_Event_On_Refusal()
    {
        var client = this.CreateClient(out _);

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => client.SendAsync(Ask("https://example.com/"), CancellationToken.None));

        await this.eventStore.Received(1).AppendAsync(
            Arg.Is<ExecutionEvent>(item => item.Type == ExecutionEventType.EgressRefused),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_Should_Refuse_A_Redirect_To_A_Host_Outside_The_Allowlist()
    {
        var options = new EgressOptions();
        options.AllowedHosts.Add("news.ycombinator.com");
        var handler = new RedirectHandler(new Uri("https://tracker.example.com/beacon"));
        var client = new HttpEgressClient(
            new HttpClient(handler),
            this.egressBudget,
            Options.Create(options),
            this.eventStore,
            new FakeTimeProvider(now),
            NullLogger<HttpEgressClient>.Instance);

        await Assert.ThrowsAsync<EgressRefusedException>(() =>
            client.SendAsync(Ask("https://news.ycombinator.com/rss"), CancellationToken.None));
    }

    [Fact]
    public async Task SendAsync_Should_Refuse_A_NonHttps_Destination()
    {
        var client = this.CreateClient(out _, allowed: "news.ycombinator.com");

        await Assert.ThrowsAsync<EgressRefusedException>(() =>
            client.SendAsync(Ask("http://news.ycombinator.com/rss"), CancellationToken.None));
    }

    [Fact]
    public async Task SendAsync_Should_Record_An_EgressFailed_Event_On_Network_Failure()
    {
        var options = new EgressOptions();
        options.AllowedHosts.Add("news.ycombinator.com");
        var client = new HttpEgressClient(
            new HttpClient(new FailingHandler()),
            this.egressBudget,
            Options.Create(options),
            this.eventStore,
            new FakeTimeProvider(now),
            NullLogger<HttpEgressClient>.Instance);

        try
        {
            await client.SendAsync(Ask("https://news.ycombinator.com/rss"), CancellationToken.None);
        }
        catch (HttpRequestException)
        {
        }

        await this.eventStore.Received(1).AppendAsync(
            Arg.Is<ExecutionEvent>(item => item.Type == ExecutionEventType.EgressFailed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_Should_Reject_A_Response_Above_The_Configured_Limit()
    {
        var client = this.CreateClient(
            out _, allowed: "news.ycombinator.com", body: "four", maxResponseBytes: 3);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.SendAsync(Ask("https://news.ycombinator.com/rss"), CancellationToken.None));
    }

    [Fact]
    public async Task SendAsync_Should_Trip_On_A_PercentEncoded_Forbidden_Fragment()
    {
        var client = this.CreateClient(
            out _, allowed: "www.youtube.com", forbidden: "steve hoff");

        await Assert.ThrowsAsync<EgressRefusedException>(() => client.SendAsync(
            Ask("https://www.youtube.com/results?search_query=steve%20hoff"),
            CancellationToken.None));
    }

    private static EgressRequest Ask(string url)
    {
        return new EgressRequest(new Uri(url), "test fetch", traceId, ExecutionOrigin.ScheduledService);
    }

    private HttpEgressClient CreateClient(
        out FakeHttpMessageHandler handler,
        string? allowed = null,
        string? forbidden = null,
        string body = "ok",
        int maxResponseBytes = 2 * 1024 * 1024)
    {
        handler = new FakeHttpMessageHandler(HttpStatusCode.OK, body);
        var options = new EgressOptions { MaxResponseBytes = maxResponseBytes };
        if (allowed is not null)
        {
            options.AllowedHosts.Add(allowed);
        }

        if (forbidden is not null)
        {
            options.ForbiddenFragments.Add(forbidden);
        }

        return new HttpEgressClient(
            new HttpClient(handler), this.egressBudget, Options.Create(options), this.eventStore,
            new FakeTimeProvider(now), NullLogger<HttpEgressClient>.Instance);
    }

    private sealed class RedirectHandler(Uri destination) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
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
}
