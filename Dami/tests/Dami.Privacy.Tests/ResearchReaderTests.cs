using System.Net;
using System.Text;
using Dami.Contracts.Events;
using Dami.Contracts.Privacy;
using Dami.Contracts.Research;
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

        public List<IPAddress?> PinnedAddresses { get; } = [];

        public List<string> UserAgents { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Requested.Add(request.RequestUri!);
            this.UserAgents.Add(request.Headers.UserAgent.ToString());
            this.PinnedAddresses.Add(request.Options.TryGetValue(ResearchConnection.PinnedAddress, out var address)
                ? address
                : null);
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

    [Fact]
    public async Task Read_Should_Preserve_Explicit_Caller_Cancellation()
    {
        using var caller = new CancellationTokenSource();
        var handler = new Canned(_ => { caller.Cancel(); throw new OperationCanceledException(caller.Token); });

        var error = await Record.ExceptionAsync(() => this.Subject(handler).ReadAsync(
            new Uri("https://1.1.1.1/"), Guid.NewGuid(), ExecutionOrigin.UserTurn, caller.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(error);
    }

    [Fact]
    public async Task Read_Should_Report_A_Source_Deadline_As_Timeout_Instead_Of_Caller_Cancellation()
    {
        var handler = new Canned(_ => throw new OperationCanceledException("Source deadline elapsed"));

        var error = await Record.ExceptionAsync(() => this.Subject(handler).ReadAsync(
            new Uri("https://1.1.1.1/"), Guid.NewGuid(), ExecutionOrigin.UserTurn, CancellationToken.None));

        Assert.IsType<TimeoutException>(error);
    }

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
    public async Task Read_Should_Pin_The_Validated_Address_On_The_Actual_Request()
    {
        var handler = new Canned(_ => Html("<p>public</p>"));

        await this.Subject(handler)
            .ReadAsync(new Uri("http://1.1.1.1/page"), Guid.NewGuid(), ExecutionOrigin.UserTurn, CancellationToken.None);

        Assert.Equal(IPAddress.Parse("1.1.1.1"), Assert.Single(handler.PinnedAddresses));
    }

    [Fact]
    public async Task Read_Should_Identify_Dami_To_Public_Apis()
    {
        var handler = new Canned(_ => Html("<p>public</p>"));

        await this.Subject(handler)
            .ReadAsync(new Uri("http://1.1.1.1/page"), Guid.NewGuid(), ExecutionOrigin.UserTurn, CancellationToken.None);

        Assert.Equal("DamiCore/1.0", Assert.Single(handler.UserAgents));
    }

    [Fact]
    public async Task Read_Should_Preserve_Typed_References_From_The_Page()
    {
        var handler = new Canned(_ => Html("""
            <html><head>
              <link rel="alternate" type="application/json" href="https://api.example.net/v1/data.json">
            </head><body>
              <a href="/reports/detail">Detailed report</a>
              <a href="#notes">Notes</a>
              <a href="mailto:desk@example.net">Email</a>
            </body></html>
            """));

        var page = await this.Subject(handler)
            .ReadAsync(new Uri("http://1.1.1.1/start"), Guid.NewGuid(), ExecutionOrigin.UserTurn, CancellationToken.None);

        Assert.Collection(
            page.References.OrderBy(reference => reference.Url.AbsoluteUri),
            reference => Assert.Equal(
                ("http://1.1.1.1/reports/detail", "Detailed report", ResearchReferenceKind.Page),
                (reference.Url.AbsoluteUri, reference.Label, reference.Kind)),
            reference => Assert.Equal(
                ("https://api.example.net/v1/data.json", "alternate", ResearchReferenceKind.Data),
                (reference.Url.AbsoluteUri, reference.Label, reference.Kind)));
    }

    [Fact]
    public async Task Read_Should_Discover_Api_And_Data_References_Inside_Json()
    {
        var handler = new Canned(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {
                  "documentation": "https://docs.example.net/guide",
                  "api": "https://api.example.net/v2/items",
                  "downloadUrl": "https://data.example.net/export/items.csv"
                }
                """, Encoding.UTF8, "application/json"),
        });

        var page = await this.Subject(handler)
            .ReadAsync(new Uri("http://1.1.1.1/catalog.json"), Guid.NewGuid(), ExecutionOrigin.UserTurn, CancellationToken.None);

        Assert.Equal(
            [ResearchReferenceKind.Page, ResearchReferenceKind.Api, ResearchReferenceKind.Data],
            page.References.Select(reference => reference.Kind));
        Assert.Equal(
            ["documentation", "api", "downloadUrl"],
            page.References.Select(reference => reference.Label));
    }

    [Fact]
    public async Task Read_Should_Not_Follow_References_From_Malformed_Json()
    {
        var handler = new Canned(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"next\":\"https://attacker.example/follow\"", Encoding.UTF8, "application/json"),
        });

        var page = await this.Subject(handler)
            .ReadAsync(new Uri("http://1.1.1.1/broken.json"), Guid.NewGuid(), ExecutionOrigin.UserTurn, CancellationToken.None);

        Assert.Empty(page.References);
        Assert.Contains("attacker.example", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_Should_Refuse_Binary_Content_Before_Reading_Its_Body()
    {
        var handler = new Canned(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([0x25, 0x50, 0x44, 0x46, 0x00, 0xff])
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf") },
            },
        });

        var refusal = await Assert.ThrowsAsync<EgressRefusedException>(() => this.Subject(handler)
            .ReadAsync(new Uri("http://1.1.1.1/file.pdf"), Guid.NewGuid(), ExecutionOrigin.UserTurn, CancellationToken.None));

        Assert.Contains("application/pdf", refusal.Message, StringComparison.Ordinal);
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
