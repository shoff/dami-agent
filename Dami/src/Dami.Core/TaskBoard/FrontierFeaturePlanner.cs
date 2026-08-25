using Dami.Contracts.Context;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Contracts.TaskBoard;

namespace Dami.Core.TaskBoard;

/// <summary>Uses the explicitly selected frontier boundary to produce a structured plan.</summary>
public sealed class FrontierFeaturePlanner : IFeaturePlanner
{
    private readonly IFrontierChat frontierChat;

    /// <summary>Creates a frontier planner.</summary>
    public FrontierFeaturePlanner(IFrontierChat frontierChat)
    {
        ArgumentNullException.ThrowIfNull(frontierChat);
        this.frontierChat = frontierChat;
    }

    /// <inheritdoc />
    public FeaturePlannerKind Kind => FeaturePlannerKind.Frontier;

    /// <inheritdoc />
    public async Task<FeaturePlanProposal> PlanAsync(
        FeaturePlanningRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Privacy != PrivacyClass.Egressable)
        {
            throw new EgressRefusedException(
                $"Feature planning privacy is {request.Privacy}; frontier requires Egressable.");
        }

        var prompt = new FrontierPrompt(
            FeaturePlanPrompt.Create(request.FeatureRequest),
            "feature planning",
            request.Privacy,
            request.RequestId,
            request.Origin);
        var response = await this.frontierChat.CompleteAsync(prompt, cancellationToken)
            .ConfigureAwait(false);
        return FeaturePlanJsonParser.Parse(response);
    }
}
