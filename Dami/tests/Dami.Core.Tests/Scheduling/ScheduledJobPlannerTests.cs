using Dami.Contracts.Models;
using Dami.Contracts.Scheduling;
using Dami.Core.Scheduling;
using Xunit;

namespace Dami.Core.Tests.Scheduling;

public sealed class ScheduledJobPlannerTests
{
    [Fact]
    public async Task PlanAsync_Should_Return_The_Models_Clarifying_Question()
    {
        var planner = new ScheduledJobPlanner(new ChatStub("""{"question":"What time should it run?","proposal":null}"""));

        var reply = await planner.PlanAsync(["Back up my notes"], CancellationToken.None);

        Assert.Equal("What time should it run?", reply.Question);
        Assert.Null(reply.Proposal);
    }

    [Fact]
    public async Task PlanAsync_Should_Return_A_Complete_Proposal_For_Confirmation()
    {
        var planner = new ScheduledJobPlanner(new ChatStub("""
            {"question":null,"proposal":{"name":"notes backup","description":"Copies notes nightly","kind":"Command","payload":"/usr/bin/rsync","arguments":["-a","/home/steve/notes/","/mnt/archive/notes/"],"cronExpression":"0 2 * * *","timeZoneId":"America/Chicago"}}
            """));

        var reply = await planner.PlanAsync(["Back up my notes", "At 2 AM every day"], CancellationToken.None);

        Assert.Equal(ScheduledJobKind.Command, Assert.IsType<ScheduledJobProposal>(reply.Proposal).Kind);
    }

    private sealed class ChatStub(string response) : IChatClient
    {
        public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken) =>
            Task.FromResult(response);

        public async IAsyncEnumerable<string> StreamAsync(
            string prompt,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
