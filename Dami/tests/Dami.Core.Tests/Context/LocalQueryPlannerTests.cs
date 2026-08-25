using Dami.Contracts.Context;
using Dami.Contracts.Models;
using Dami.Core.Context;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Context;

/// <summary>Covers ADR-0019's planning pass: route, ground, expand — and fail open.</summary>
public sealed class LocalQueryPlannerTests
{
    private readonly IChatClient chatClient = Substitute.For<IChatClient>();

    [Fact]
    public async Task PlanAsync_Should_Keep_The_Request_Among_The_Searches()
    {
        this.Answers("""{"searches": ["aortic stenosis"], "domains": []}""");

        var plan = await this.Create().PlanAsync("what should I ask the surgeon", CancellationToken.None);

        Assert.Contains("what should I ask the surgeon", plan.Searches);
    }

    [Fact]
    public async Task PlanAsync_Should_Reground_Searches_In_The_Facts_A_Named_Domain_Holds()
    {
        this.Answers(
            """{"searches": ["heart condition treatment"], "domains": ["health"]}""",
            """{"searches": ["severe aortic stenosis", "mechanical AVR surgery"]}""");

        var plan = await this.Create(FactSource("health", "Severe aortic stenosis"))
            .PlanAsync("given my heart condition what should I ask the surgeon", CancellationToken.None);

        // The measured point of the second pass: the first draft says "heart condition",
        // which matches nothing the corpus wrote; the regrounded one says what it says.
        Assert.Contains("severe aortic stenosis", plan.Searches);
        Assert.DoesNotContain("heart condition treatment", plan.Searches);
        Assert.Single(plan.Facts);
    }

    [Fact]
    public async Task PlanAsync_Should_Not_Reground_When_No_Domain_Was_Named()
    {
        this.Answers("""{"searches": ["dinner plans"], "domains": []}""");

        var plan = await this.Create(FactSource("health", "Severe aortic stenosis"))
            .PlanAsync("what is for dinner", CancellationToken.None);

        // One pass, not two: a question that touches no domain must not pay for grounding.
        await this.chatClient.Received(1).CompleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Contains("dinner plans", plan.Searches);
        Assert.Empty(plan.Facts);
    }

    [Fact]
    public async Task PlanAsync_Should_Keep_The_Draft_When_Regrounding_Fails()
    {
        this.Answers(
            """{"searches": ["heart condition treatment"], "domains": ["health"]}""",
            "the model rambled instead of answering");

        var plan = await this.Create(FactSource("health", "Severe aortic stenosis"))
            .PlanAsync("what should I ask the surgeon", CancellationToken.None);

        // Grounding improves a draft; it is not a precondition for having one.
        Assert.Contains("heart condition treatment", plan.Searches);
    }

    [Fact]
    public async Task PlanAsync_Should_Fall_Back_To_The_Request_When_The_Model_Is_Unparseable()
    {
        this.Answers("I cannot help with that.");

        var plan = await this.Create().PlanAsync("what should I ask the surgeon", CancellationToken.None);

        // Fails open, unlike the disclosure gate: the cost of a bad plan is a worse search,
        // so it degrades to exactly what retrieval did before planning existed.
        Assert.Equal(["what should I ask the surgeon"], plan.Searches);
    }

    [Fact]
    public async Task PlanAsync_Should_Fall_Back_When_The_Model_Throws()
    {
        this.chatClient.CompleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new HttpRequestException("sidecar down"));

        var plan = await this.Create().PlanAsync("what should I ask the surgeon", CancellationToken.None);

        Assert.Equal(["what should I ask the surgeon"], plan.Searches);
    }

    [Fact]
    public async Task PlanAsync_Should_Drop_A_Domain_The_Model_Invented()
    {
        this.Answers("""{"searches": ["taxes"], "domains": ["finance"]}""");

        var plan = await this.Create().PlanAsync("what do I owe", CancellationToken.None);

        // Only known domains survive, or a hallucinated name becomes a lookup that
        // silently matches nothing and a plan that claims coverage it does not have.
        Assert.Empty(plan.Domains);
    }

    [Fact]
    public async Task PlanAsync_Should_Search_The_Request_Verbatim_When_Disabled()
    {
        var plan = await this.Create(enabled: false)
            .PlanAsync("what should I ask the surgeon", CancellationToken.None);

        Assert.Equal(["what should I ask the surgeon"], plan.Searches);
        await this.chatClient.DidNotReceive().CompleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private void Answers(params string[] replies)
    {
        var index = 0;
        this.chatClient.CompleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(replies[Math.Min(index++, replies.Length - 1)]));
    }

    private static IStructuredFactSource FactSource(string domain, params string[] facts)
    {
        var source = Substitute.For<IStructuredFactSource>();
        source.Domain.Returns(domain);
        source.RelevantAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(AsFactsAsync(facts));
        return source;
    }

    private static async IAsyncEnumerable<StructuredFact> AsFactsAsync(IEnumerable<string> facts)
    {
        foreach (var fact in facts)
        {
            yield return new StructuredFact(Guid.NewGuid(), fact, new DateOnly(2026, 3, 2), "diagnosis");
        }

        await Task.CompletedTask;
    }

    private LocalQueryPlanner Create(IStructuredFactSource? source = null, bool enabled = true)
    {
        return new LocalQueryPlanner(
            this.chatClient,
            source is null ? [] : [source],
            Options.Create(new QueryPlanOptions { Enabled = enabled }),
            NullLogger<LocalQueryPlanner>.Instance);
    }
}
