using System.Net;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Privacy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Providers.Tests;

/// <summary>ADR-0010's gate: the adapter enforces what the router should make unreachable.</summary>
public sealed class AnthropicChatClientTests
{
    private const string ANSWER_JSON = """
        {"content":[{"type":"text","text":"a frontier answer"}]}
        """;

    private static readonly DateTimeOffset now = new(2026, 8, 23, 13, 0, 0, TimeSpan.Zero);
    private static readonly Guid traceId = Guid.NewGuid();

    private readonly IExecutionEventStore eventStore = Substitute.For<IExecutionEventStore>();

    [Fact]
    public async Task CompleteAsync_Should_Refuse_A_LocalOnly_Prompt()
    {
        var client = this.CreateClient(out _, allowlisted: true, apiKey: "key");

        await Assert.ThrowsAsync<EgressRefusedException>(() => client.CompleteAsync(
            Prompt(PrivacyClass.LocalOnly), CancellationToken.None));
    }

    [Fact]
    public async Task CompleteAsync_Should_Not_Reach_The_Network_When_Refusing()
    {
        var client = this.CreateClient(out var handler, allowlisted: true, apiKey: "key");

        await Assert.ThrowsAsync<EgressRefusedException>(() => client.CompleteAsync(
            Prompt(PrivacyClass.LocalOnly), CancellationToken.None));

        Assert.Empty(handler.Sent);
    }

    [Fact]
    public async Task CompleteAsync_Should_Refuse_When_The_Provider_Host_Is_Not_Allowlisted()
    {
        var client = this.CreateClient(out _, allowlisted: false, apiKey: "key");

        await Assert.ThrowsAsync<EgressRefusedException>(() => client.CompleteAsync(
            Prompt(PrivacyClass.Egressable), CancellationToken.None));
    }

    [Fact]
    public async Task CompleteAsync_Should_Refuse_Without_An_Api_Key()
    {
        var client = this.CreateClient(out _, allowlisted: true, apiKey: "");

        await Assert.ThrowsAsync<EgressRefusedException>(() => client.CompleteAsync(
            Prompt(PrivacyClass.Egressable), CancellationToken.None));
    }

    [Fact]
    public async Task CompleteAsync_Should_Return_The_Text_For_An_Allowed_Call()
    {
        var client = this.CreateClient(out _, allowlisted: true, apiKey: "key");

        var answer = await client.CompleteAsync(Prompt(PrivacyClass.Egressable), CancellationToken.None);

        Assert.Equal("a frontier answer", answer);
    }

    [Fact]
    public async Task CompleteAsync_Should_Record_The_Refusal_As_An_Event()
    {
        var client = this.CreateClient(out _, allowlisted: true, apiKey: "key");

        await Assert.ThrowsAsync<EgressRefusedException>(() => client.CompleteAsync(
            Prompt(PrivacyClass.LocalOnly), CancellationToken.None));

        await this.eventStore.Received(1).AppendAsync(
            Arg.Is<ExecutionEvent>(item =>
                item.Type == ExecutionEventType.EgressRefused && item.TraceId == traceId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteAsync_Should_Never_Put_The_Prompt_Text_In_An_Event_Label()
    {
        var client = this.CreateClient(out _, allowlisted: true, apiKey: "key");

        await client.CompleteAsync(
            Prompt(PrivacyClass.Egressable, prompt: "the secret prompt body"), CancellationToken.None);

        await this.eventStore.DidNotReceive().AppendAsync(
            Arg.Is<ExecutionEvent>(item => item.Label.Contains("the secret prompt body")),
            Arg.Any<CancellationToken>());
    }

    private static FrontierPrompt Prompt(PrivacyClass privacy, string prompt = "a question")
    {
        return new FrontierPrompt(
            prompt, "test purpose", privacy, traceId, ExecutionOrigin.UserTurn);
    }

    private AnthropicChatClient CreateClient(
        out RecordingHandler handler,
        bool allowlisted,
        string apiKey)
    {
        handler = new RecordingHandler(ANSWER_JSON);
        var egress = new EgressOptions();
        if (allowlisted)
        {
            egress.AllowedHosts.Add("api.anthropic.com");
        }

        return new AnthropicChatClient(
            new HttpClient(handler),
            Options.Create(new AnthropicOptions { ApiKey = apiKey }),
            Options.Create(egress),
            this.eventStore,
            new FakeTimeProvider(now),
            NullLogger<AnthropicChatClient>.Instance);
    }

    /// <summary>Records requests and answers with a canned Anthropic-shaped body.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string body;

        public RecordingHandler(string body)
        {
            this.body = body;
        }

        public List<Uri> Sent { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            this.Sent.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(this.body),
            });
        }
    }
}
