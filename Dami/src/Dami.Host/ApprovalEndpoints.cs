using Dami.Authentication;
using Dami.Contracts.Approvals;
using Dami.Core.Approvals;

namespace Dami.Host;

/// <summary>The approval queue — and, on approval, the execution it gates.</summary>
/// <remarks>
/// Execution lives here, in the runtime, not in any client (D-005): a thin client says
/// yes or no; what "yes" *does* — moving files, egressing a brief — is the runtime's
/// job, behind the same single-resolution guarantee as ever.
/// </remarks>
public static class ApprovalEndpoints
{
    /// <summary>Maps the approval routes.</summary>
    public static void Map(WebApplication app)
    {
        app.MapGet("/approvals", (IApprovalService approvals, CancellationToken token) =>
            Results.Ok(Collect.Async(approvals.PendingAsync(token))));
        MapResolve(app);
    }

    private static void MapResolve(WebApplication app)
    {
        app.MapPost("/approvals/{prefix}/resolve", async (
            string prefix, ResolveRequest request, IApprovalService approvals,
            ApprovalExecutionDispatcher dispatcher, TimeProvider clock,
            CancellationToken token) =>
        {
            var pending = await ResolveAsync(approvals, prefix, token).ConfigureAwait(false);
            if (pending is null)
            {
                return Results.NotFound();
            }

            return await ResolveAndExecuteAsync(
                pending, request, approvals, dispatcher, clock, token).ConfigureAwait(false);
        }).RequireAuthorization(DamiAuthorizationPolicies.APPROVALS_RESOLVE);
    }

    private static async Task<IResult> ResolveAndExecuteAsync(
        ApprovalRequest pending,
        ResolveRequest request,
        IApprovalService approvals,
        ApprovalExecutionDispatcher dispatcher,
        TimeProvider clock,
        CancellationToken token)
    {
        var status = request.Approve ? ApprovalStatus.Approved : ApprovalStatus.Denied;
        var note = request.Note ?? (request.Approve ? "approved via API" : "denied via API");
        var resolved = await approvals.ResolveAsync(
            pending.ApprovalId, status, note, clock.GetUtcNow(), token).ConfigureAwait(false);
        if (!resolved)
        {
            return Results.Conflict();
        }

        var execution = request.Approve
            ? await dispatcher.ExecuteAsync(pending, token).ConfigureAwait(false)
            : null;
        return Results.Ok(new
        {
            approvalId = pending.ApprovalId,
            action = pending.Action,
            status = status.ToString(),
            execution,
        });
    }

    private static async Task<ApprovalRequest?> ResolveAsync(
        IApprovalService approvals,
        string prefix,
        CancellationToken cancellationToken)
    {
        await foreach (var request in approvals.PendingAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Collect.Matches(request.ApprovalId, prefix))
            {
                return request;
            }
        }

        return null;
    }
}
