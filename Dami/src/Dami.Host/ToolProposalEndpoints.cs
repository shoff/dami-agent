using Dami.Capabilities.Sandboxed;
using Dami.Contracts.ToolStaging;

namespace Dami.Host;

internal static class ToolProposalEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/tool-proposals", ListAsync);
        app.MapGet("/tool-proposals/{proposalId:guid}", InspectAsync);
        app.MapPost("/tool-proposals/{proposalId:guid}/verify", VerifyAsync);
        app.MapPost("/tool-proposals/{proposalId:guid}/promote", PromoteAsync);
    }

    private static async Task<IResult> ListAsync(
        int? limit,
        IToolProposalStore store,
        CancellationToken cancellationToken)
    {
        int requested = limit ?? ToolProposalReviewLimits.DEFAULT_LIST_LIMIT;
        if (requested is <= 0 or > ToolProposalReviewLimits.MAX_LIST_LIMIT)
        {
            return Results.BadRequest();
        }

        IReadOnlyList<ToolProposalSummary> proposals = await store
            .ListAsync(requested, cancellationToken).ConfigureAwait(false);
        return Results.Ok(proposals);
    }

    private static async Task<IResult> InspectAsync(
        Guid proposalId,
        IToolProposalStore store,
        CancellationToken cancellationToken)
    {
        StagedToolProposal? proposal = await store
            .FindAsync(proposalId, cancellationToken).ConfigureAwait(false);
        return proposal is null ? Results.NotFound() : Results.Ok(proposal);
    }

    private static async Task<IResult> VerifyAsync(
        Guid proposalId,
        ExactToolVersionRequest request,
        IToolPromotionWorkflow workflow,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(() => workflow.VerifyAsync(
            proposalId, request.ArtifactVersion, cancellationToken)).ConfigureAwait(false);
    }

    private static async Task<IResult> PromoteAsync(
        Guid proposalId,
        ExactToolVersionRequest request,
        IToolPromotionWorkflow workflow,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(() => workflow.RequestPromotionAsync(
            proposalId, request.ArtifactVersion, cancellationToken)).ConfigureAwait(false);
    }

    private static async Task<IResult> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return Results.Ok(await operation().ConfigureAwait(false));
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }
}

/// <summary>An exact immutable tool version selected after review.</summary>
public sealed record ExactToolVersionRequest(string ArtifactVersion);
