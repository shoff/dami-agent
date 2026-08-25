using Dami.Contracts.Models;
using Dami.Contracts.TaskBoard;
using Dami.Contracts.Events;
using Dami.Contracts.Context;
using Dami.Core.TaskBoard;
using Xunit;

namespace Dami.Core.Tests.TaskBoard;

public sealed class LocalFeaturePlannerTests
{
    [Fact]
    public async Task PlanAsync_Should_Request_Strict_Json_And_Parse_A_Recursive_Proposal()
    {
        var chat = new StubChatClient(RESPONSE);
        var planner = new LocalFeaturePlanner(chat);
        var request = new FeaturePlanningRequest(
            Guid.NewGuid(), "Build the dashboard", new TaskActor("steve", TaskActorKind.Human),
            new DateTimeOffset(2026, 8, 24, 21, 30, 0, TimeSpan.Zero),
            FeaturePlannerKind.Local, PrivacyClass.LocalOnly, ExecutionOrigin.UserTurn);

        var proposal = await planner.PlanAsync(request, CancellationToken.None);

        Assert.Contains("Build the dashboard", chat.Prompt, StringComparison.Ordinal);
        Assert.Contains("Return only JSON", chat.Prompt, StringComparison.Ordinal);
        Assert.Equal("Dashboard", proposal.Title);
        var root = Assert.Single(proposal.Tasks);
        Assert.Equal(TaskPriority.High, root.Priority);
        Assert.Equal("child", Assert.Single(root.SubTasks).Key);
    }

    private const string RESPONSE = """
        {
          "title": "Dashboard",
          "plan": "Build it vertically.",
          "rootOrdering": "Ordered",
          "tasks": [{
            "key": "root",
            "title": "Root",
            "description": "Own the slice",
            "priority": "High",
            "position": 0,
            "subTaskOrdering": "Priority",
            "prerequisiteKeys": [],
            "acceptanceCriteria": ["Visible live"],
            "subTasks": [{
              "key": "child",
              "title": "Child",
              "description": "Implement it",
              "priority": "Normal",
              "position": 0,
              "subTaskOrdering": "Ordered",
              "prerequisiteKeys": [],
              "acceptanceCriteria": [],
              "subTasks": []
            }]
          }]
        }
        """;

    private sealed class StubChatClient : IChatClient
    {
        private readonly string response;

        internal StubChatClient(string response)
        {
            this.response = response;
        }

        internal string Prompt { get; private set; } = string.Empty;

        public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken)
        {
            this.Prompt = prompt;
            return Task.FromResult(this.response);
        }

        public async IAsyncEnumerable<string> StreamAsync(
            string prompt,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }
    }
}
