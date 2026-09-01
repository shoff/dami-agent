using System.Net;
using System.Text;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Privacy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Dami.Providers.Tests;

public sealed class OpenAiImageGeneratorTests
{
    private static readonly string onePixel = Convert.ToBase64String(
        Encoding.UTF8.GetBytes("pretend-png-bytes"));

    private sealed class Canned : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string Body { get; set; } = string.Empty;

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        /// <summary>What the caller actually put on the wire.</summary>
        public string Sent { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Request = request;
            if (request.Content is not null)
            {
                this.Sent = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(this.Status)
            {
                Content = new StringContent(this.Body),
            };
        }
    }

    private static ImageRequest Request(PrivacyClass privacy = PrivacyClass.Egressable) =>
        new("a portrait", "daily portrait (evening)", privacy, Guid.NewGuid(),
            ExecutionOrigin.ScheduledService);

    private static (OpenAiImageGenerator Generator, Canned Handler, IExecutionEventStore Events)
        Create(string? apiKey = "sk-test", string allowedHost = "api.openai.com",
               string? budgetRefusal = null)
    {
        var handler = new Canned
        {
            Body = $$"""{"data":[{"b64_json":"{{onePixel}}"}]}""",
        };
        var egress = new EgressOptions();
        if (allowedHost.Length > 0)
        {
            egress.AllowedHosts.Add(allowedHost);
        }

        var events = Substitute.For<IExecutionEventStore>();
        var budget = Substitute.For<IEgressBudget>();
        budget.FindRefusalAsync(Arg.Any<CancellationToken>()).Returns(budgetRefusal);
        return (
            new OpenAiImageGenerator(
                new HttpClient(handler),
                Options.Create(new OpenAiImageOptions { ApiKey = apiKey ?? string.Empty }),
                Options.Create(egress),
                budget,
                events,
                TimeProvider.System,
                NullLogger<OpenAiImageGenerator>.Instance),
            handler,
            events);
    }

    [Fact]
    public async Task Should_Return_The_Decoded_Image()
    {
        var (generator, _, _) = Create();

        var image = await generator.GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal("pretend-png-bytes", Encoding.UTF8.GetString(image.Bytes.ToArray()));
    }

    [Fact]
    public async Task Should_Refuse_A_Prompt_That_Is_Not_Egressable()
    {
        // The caller should make this unreachable; the boundary enforces it anyway.
        var (generator, _, _) = Create();

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => generator.GenerateAsync(Request(PrivacyClass.LocalOnly), CancellationToken.None));
    }

    [Fact]
    public async Task Should_Refuse_A_Host_That_Is_Not_Allowlisted()
    {
        // Being configured does not exempt a provider from the allowlist.
        var (generator, _, _) = Create(allowedHost: string.Empty);

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => generator.GenerateAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task Should_Refuse_When_No_Key_Is_Configured()
    {
        // Absent capability, not an error to retry.
        var (generator, _, _) = Create(apiKey: null);

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => generator.GenerateAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task Should_Not_Reach_The_Network_When_Refused()
    {
        var (generator, handler, _) = Create(allowedHost: string.Empty);

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => generator.GenerateAsync(Request(), CancellationToken.None));

        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task Should_Record_A_Refusal_In_The_Event_Stream()
    {
        // Every call, allowed or refused, is auditable. The Hermes job recorded nothing.
        var (generator, _, events) = Create(allowedHost: string.Empty);

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => generator.GenerateAsync(Request(), CancellationToken.None));

        await events.Received().AppendAsync(
            Arg.Is<ExecutionEvent>(e => e.Type == ExecutionEventType.EgressRefused),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Record_A_Completed_Egress_On_Success()
    {
        var (generator, _, events) = Create();

        await generator.GenerateAsync(Request(), CancellationToken.None);

        await events.Received().AppendAsync(
            Arg.Is<ExecutionEvent>(e => e.Type == ExecutionEventType.EgressCompleted),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Never_Put_The_Prompt_Into_An_Event_Label()
    {
        // Purpose lines, never prompt text — the same rule the frontier client follows.
        var (generator, _, events) = Create();

        await generator.GenerateAsync(Request(), CancellationToken.None);

        await events.DidNotReceive().AppendAsync(
            Arg.Is<ExecutionEvent>(e => e.Label.Contains("a portrait", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Send_The_Key_As_A_Bearer_Token()
    {
        var (generator, handler, _) = Create();

        await generator.GenerateAsync(Request(), CancellationToken.None);

        Assert.Contains(
            "sk-test",
            handler.Request!.Headers.GetValues("Authorization").First(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Read_Should_Fail_Loudly_When_The_Provider_Returns_No_Image()
    {
        // A 200 with an empty data array is the shape a quota or safety refusal takes.
        Assert.Throws<InvalidOperationException>(
            () => OpenAiImageGenerator.Read("""{"data":[]}""", Request()));
    }
    [Fact]
    public async Task Should_Send_The_Prompt_As_The_Prompt()
    {
        // Mutation testing found this untested: swapping Prompt for Purpose in the body
        // sends the wrong string to a paid API and no test noticed.
        var (generator, handler, _) = Create();

        await generator.GenerateAsync(Request(), CancellationToken.None);

        Assert.Contains("\"prompt\":\"a portrait\"", handler.Sent, StringComparison.Ordinal);
    }
    [Fact]
    public async Task Should_Refuse_When_The_Egress_Budget_Is_Spent()
    {
        // C5. This is the only door with a per-call bill and it was the only
        // frontier-class door not behind the budget — the subscription door, whose
        // marginal cost is zero, was.
        var (generator, _, _) = Create(budgetRefusal: "egress budget exhausted");

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => generator.GenerateAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task Should_Not_Spend_Money_When_The_Budget_Refuses()
    {
        var (generator, handler, _) = Create(budgetRefusal: "egress budget exhausted");

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => generator.GenerateAsync(Request(), CancellationToken.None));

        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task Should_Record_EgressFailed_When_The_Provider_Errors()
    {
        // The prompt is already on the wire by then; a dangling EgressRequested with no
        // outcome is indistinguishable from a call that never happened.
        var (generator, handler, events) = Create();
        handler.Status = HttpStatusCode.InternalServerError;

        await Assert.ThrowsAsync<HttpRequestException>(
            () => generator.GenerateAsync(Request(), CancellationToken.None));

        await events.Received().AppendAsync(
            Arg.Is<ExecutionEvent>(e => e.Type == ExecutionEventType.EgressFailed),
            Arg.Any<CancellationToken>());
    }
}
