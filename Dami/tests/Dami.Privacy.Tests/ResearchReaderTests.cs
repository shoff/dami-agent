using System.Net;
using Dami.Contracts.Events;
using Dami.Contracts.Privacy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Dami.Privacy.Tests;

/// <summary>Read-only, public only, capped, recorded (ADR-0033).</summary>
public sealed class ResearchReaderTests
{
    private readonly IEgressBudget budget = Substitute.For<IEgressBudget>();
    private readonly IExecutionEventStore events = Substitute.For<IExecutionEventStore>();

    private sealed class Canned(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<Uri> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Requested.Add(request.RequestUri!);
            return Task.FromResult(answer(request));
        }
    }

    private ResearchReader Subject(Canned handler, bool enabled = true, int maxBytes = 1_000_000)
    {
        this.budget.FindRefusalAsync(Arg.Any<CancellationToken>()).Returns((string?)null);
        return new ResearchReader(
            new HttpClient(handler), this.budget,
            Options.Create(new ResearchOptions { Enabled = enabled, MaxResponseBytes = maxBytes, BlockedHosts = { "example.org" } }),
            this.events, TimeProvider.System, NullLogger<ResearchReader>.Instance);
    }

    private static HttpResponseMessage Html(string html) => new(HttpStatusCode.OK) { Content = new StringContent(html) };

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.0.0.5", false)]
    [InlineData("192.168.4.23", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("169.254.1.1", false)]
    [InlineData("::1", false)]
    [InlineData("1.1.1.1", true)]
    [InlineData("93.184.216.34", true)]
    public void IsPublic_Should_Know_This_Network_From_The_Internet(string address, bool expected)
    {
        Assert.Equal(expected, ResearchReader.IsPublic(IPAddress.Parse(address)));
    }

    [Fact]
    public async Task Read_Should_Refuse_A_Private_Address_Before_Sending_Anything()
    {
        // A fetcher that will read http://192.168.4.23:5000/ is a scanner of this network.
        var handler = new Canned(_ => Html("<p>secret</p>"));

        await Assert.ThrowsAsync<EgressRefusedException>(() => this.Subject(handler)
            .ReadAsync(new Uri("http://192.168.4.23:5000/"), Guid.NewGuid(), ExecutionOrigin.UserTurn, CancellationToken.None));

        Assert.Empty(handler.Requested);
        await this.events.Received().AppendAsync(Arg.Is<ExecutionEvent>(e => e.Type == ExecutionEventType.EgressRefused), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Read_Should_Refuse_A_Redirect_Into_This_Network()
    {
        var handler = new Canned(request => request.RequestUri!.Host == "1.1.1.1"
            ? new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("http://127.0.0.1:5810/health") } }
            : Html("<p>internal</p>"));

        await Assert.ThrowsAsync<EgressRefusedException>(() => this.Subject(handler)
            .ReadAsync(new Uri("http://1.1.1.1/"), Guid.NewGuid(), ExecutionOrigin.UserTurn, CancellationToken.None));

        Assert.Single(handler.Requested);
    }

    [Fact]
    public async Task Read_Should_Return_The_Text_And_Record_The_Read()
    {
        var handler = new Canned(_ => Html("<html><title>Listing</title><body><p>Rate $120</p></body></html>"));

        var page = await this.Subject(handler)
            .ReadAsync(new Uri("http://1.1.1.1/job"), Guid.NewGuid(), ExecutionOrigin.UserTurn, CancellationToken.None);

        Assert.Equal(("Listing", "Rate $120", 200), (page.Title, page.Text, page.StatusCode));
        await this.events.Received().AppendAsync(Arg.Is<ExecutionEvent>(e => e.Type == ExecutionEventType.EgressCompleted), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Read_Should_Refuse_When_Disabled_Or_Blocked_Or_Not_Http()
    {
        var handler = new Canned(_ => Html("<p>x</p>"));

        await Assert.ThrowsAsync<EgressRefusedException>(() => this.Subject(handler, enabled: false)
            .ReadAsync(new Uri("http://1.1.1.1/"), Guid.NewGuid(), ExecutionOrigin.UserTurn, CancellationToken.None));
        await Assert.ThrowsAsync<EgressRefusedException>(() => this.Subject(handler)
            .ReadAsync(new Uri("https://www.example.org/"), Guid.NewGuid(), ExecutionOrigin.UserTurn, CancellationToken.None));
        await Assert.ThrowsAsync<EgressRefusedException>(() => this.Subject(handler)
            .ReadAsync(new Uri("ftp://1.1.1.1/"), Guid.NewGuid(), ExecutionOrigin.UserTurn, CancellationToken.None));

        Assert.Empty(handler.Requested);
    }

    [Fact]
    public async Task Read_Should_Refuse_A_Page_Bigger_Than_The_Cap()
    {
        var handler = new Canned(_ =>
        {
            var response = Html(new string('x', 5000));
            response.Content.Headers.ContentLength = 5000;
            return response;
        });

        await Assert.ThrowsAsync<EgressRefusedException>(() => this.Subject(handler, maxBytes: 1000)
            .ReadAsync(new Uri("http://1.1.1.1/big"), Guid.NewGuid(), ExecutionOrigin.UserTurn, CancellationToken.None));
    }
}
