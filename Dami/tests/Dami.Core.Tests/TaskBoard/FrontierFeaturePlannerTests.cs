using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Dami.Contracts.TaskBoard;
using Dami.Contracts.Privacy;
using Dami.Core.TaskBoard;
using Xunit;

namespace Dami.Core.Tests.TaskBoard;

public sealed class FrontierFeaturePlannerTests
{
    [Fact]
    public async Task PlanAsync_Should_Use_An_Explicit_Egressable_Audited_Prompt()
    {
        var frontier = new StubFrontierChat();
        var planner = new FrontierFeaturePlanner(frontier);
        var request = new FeaturePlanningRequest(
            Guid.NewGuid(), "Plan external work",
            new TaskActor("dami", TaskActorKind.Agent),
            new DateTimeOffset(2026, 8, 24, 22, 0, 0, TimeSpan.Zero),
            FeaturePlannerKind.Frontier, PrivacyClass.Egressable,
            ExecutionOrigin.ReactiveTrigger);

        var result = await planner.PlanAsync(request, CancellationToken.None);

        Assert.Equal("Frontier plan", result.Title);
        Assert.NotNull(frontier.Prompt);
        Assert.Equal((PrivacyClass.Egressable, request.RequestId, request.Origin),
            (frontier.Prompt.Privacy, frontier.Prompt.TraceId, frontier.Prompt.Origin));
        Assert.Equal("feature planning", frontier.Prompt.Purpose);
        Assert.Contains(request.FeatureRequest, frontier.Prompt.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanAsync_Should_Refuse_LocalOnly_Before_Calling_The_Frontier()
    {
        var frontier = new StubFrontierChat();
        var planner = new FrontierFeaturePlanner(frontier);
        var request = new FeaturePlanningRequest(
            Guid.NewGuid(), "private feature", new TaskActor("dami", TaskActorKind.Agent),
            new DateTimeOffset(2026, 8, 24, 22, 5, 0, TimeSpan.Zero),
            FeaturePlannerKind.Frontier, PrivacyClass.LocalOnly, ExecutionOrigin.ReactiveTrigger);

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => planner.PlanAsync(request, CancellationToken.None));

        Assert.Equal(0, frontier.Calls);
    }

    private sealed class StubFrontierChat : IFrontierChat
    {
        internal FrontierPrompt Prompt { get; private set; } = null!;

        internal int Calls { get; private set; }

        public Task<string> CompleteAsync(
            FrontierPrompt prompt,
            CancellationToken cancellationToken)
        {
            this.Calls++;
            this.Prompt = prompt;
            return Task.FromResult("""
                {"title":"Frontier plan","plan":"Do it.","rootOrdering":"Priority",
                 "tasks":[]}
                """);
        }
    }
}
