using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Providers.Tests;

/// <summary>The subscription frontier's gate: refuse before spawning, sandbox always.</summary>
public sealed class CodexChatClientTests
{
    private readonly IEgressBudget egressBudget = Substitute.For<IEgressBudget>();

    public CodexChatClientTests()
    {
        // NSubstitute's auto-stub for Task<string?> is "", which reads as a refusal.
        this.egressBudget.FindRefusalAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));
    }

    private static readonly DateTimeOffset now = new(2026, 8, 23, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid traceId = Guid.NewGuid();

    private readonly ICodexProcess codexProcess = Substitute.For<ICodexProcess>();
    private readonly IExecutionEventStore eventStore = Substitute.For<IExecutionEventStore>();

    [Fact]
    public async Task CompleteAsync_Should_Refuse_A_LocalOnly_Prompt_Without_Spawning()
    {
        var client = this.CreateClient(enabled: true);

        await Assert.ThrowsAsync<EgressRefusedException>(() => client.CompleteAsync(
            Prompt(PrivacyClass.LocalOnly), CancellationToken.None));

        await this.codexProcess.DidNotReceiveWithAnyArgs().RunAsync(
            default!, default!, default, default);
    }

    [Fact]
    public async Task CompleteAsync_Should_Refuse_When_The_Budget_Is_Exhausted_Without_Spawning()
    {
        this.egressBudget.FindRefusalAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("Egress budget exhausted"));
        var client = this.CreateClient(enabled: true);

        await Assert.ThrowsAsync<EgressRefusedException>(() => client.CompleteAsync(
            Prompt(PrivacyClass.Egressable), CancellationToken.None));

        await this.codexProcess.DidNotReceiveWithAnyArgs().RunAsync(
            default!, default!, default, default);
    }

    [Fact]
    public async Task CompleteAsync_Should_Refuse_When_Disabled()
    {
        var client = this.CreateClient(enabled: false);

        await Assert.ThrowsAsync<EgressRefusedException>(() => client.CompleteAsync(
            Prompt(PrivacyClass.Egressable), CancellationToken.None));
    }

    [Fact]
    public async Task CompleteAsync_Should_Return_The_Subprocess_Answer()
    {
        this.ArrangeAnswer("a frontier answer");
        var client = this.CreateClient(enabled: true);

        var answer = await client.CompleteAsync(Prompt(PrivacyClass.Egressable), CancellationToken.None);

        Assert.Equal("a frontier answer", answer);
    }

    [Fact]
    public async Task CompleteAsync_Should_Always_Run_Read_Only_Outside_Any_Repo()
    {
        this.ArrangeAnswer("ok");
        IReadOnlyList<string>? arguments = null;
        this.codexProcess.RunAsync(
                Arg.Any<string>(), Arg.Do<IReadOnlyList<string>>(value => arguments = value),
                Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns("ok");
        var client = this.CreateClient(enabled: true);

        await client.CompleteAsync(Prompt(PrivacyClass.Egressable), CancellationToken.None);

        Assert.NotNull(arguments);
        Assert.Equal(
            (true, true),
            (arguments.Contains("read-only"), arguments.Contains("--skip-git-repo-check")));
    }

    [Fact]
    public async Task CompleteAsync_Should_Record_Egress_Events_Without_The_Prompt_Text()
    {
        this.ArrangeAnswer("ok");
        var client = this.CreateClient(enabled: true);

        await client.CompleteAsync(
            Prompt(PrivacyClass.Egressable, prompt: "the secret question"), CancellationToken.None);

        await this.eventStore.DidNotReceive().AppendAsync(
            Arg.Is<ExecutionEvent>(item => item.Label.Contains("the secret question")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteAsync_Should_Record_The_Refusal_In_The_Trace()
    {
        var client = this.CreateClient(enabled: false);

        await Assert.ThrowsAsync<EgressRefusedException>(() => client.CompleteAsync(
            Prompt(PrivacyClass.Egressable), CancellationToken.None));

        await this.eventStore.Received(1).AppendAsync(
            Arg.Is<ExecutionEvent>(item =>
                item.Type == ExecutionEventType.EgressRefused && item.TraceId == traceId),
            Arg.Any<CancellationToken>());
    }

    private void ArrangeAnswer(string answer)
    {
        this.codexProcess.RunAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(answer);
    }

    private static FrontierPrompt Prompt(PrivacyClass privacy, string prompt = "a question")
    {
        return new FrontierPrompt(prompt, "test purpose", privacy, traceId, ExecutionOrigin.UserTurn);
    }

    private CodexChatClient CreateClient(bool enabled)
    {
        return new CodexChatClient(
            this.codexProcess,
            Options.Create(new CodexOptions { Enabled = enabled }),
            this.eventStore,
            this.egressBudget,
            new FakeTimeProvider(now),
            NullLogger<CodexChatClient>.Instance);
    }
}
