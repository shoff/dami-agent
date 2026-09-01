using Dami.Contracts.Scheduling;
using Dami.Core.Scheduling;

namespace Dami.Host;

internal static class ScheduledJobEndpoints
{
    internal static void MapScheduledJobs(this WebApplication app)
    {
        app.MapGet("/jobs", (IScheduledJobStore store, CancellationToken cancellationToken) =>
            store.ListAsync(cancellationToken));
        app.MapPost("/jobs/plan", (
            ScheduledJobConversation request,
            ScheduledJobPlanner planner,
            CancellationToken cancellationToken) =>
            planner.PlanAsync(request.Messages, cancellationToken));
        app.MapPost("/jobs/drafts", (
            ScheduledJobProposal proposal,
            ScheduledJobService service,
            CancellationToken cancellationToken) =>
            service.CreateDraftAsync(proposal, cancellationToken));
        app.MapPost("/jobs/{jobId:guid}/confirm", (
            Guid jobId,
            ScheduledJobService service,
            CancellationToken cancellationToken) =>
            service.ConfirmAsync(jobId, cancellationToken));
    }

    internal sealed record ScheduledJobConversation(IReadOnlyList<string> Messages);
}
