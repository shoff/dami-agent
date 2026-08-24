using Dami.Contracts.ToolStaging;

namespace Dami.Host;

internal static class ToolProposalEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/tool-proposals", ListAsync);
        app.MapGet("/tool-proposals/{proposalId:guid}", InspectAsync);
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
}
