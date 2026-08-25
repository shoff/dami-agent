using Dami.Contracts.Models;
using Dami.Contracts.TaskBoard;

namespace Dami.Core.TaskBoard;

/// <summary>Uses the private loopback model to produce a structured feature plan.</summary>
public sealed class LocalFeaturePlanner : IFeaturePlanner
{
    private readonly IChatClient chatClient;

    /// <summary>Creates a local planner.</summary>
    public LocalFeaturePlanner(IChatClient chatClient)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        this.chatClient = chatClient;
    }

    /// <inheritdoc />
    public FeaturePlannerKind Kind => FeaturePlannerKind.Local;

    /// <inheritdoc />
    public async Task<FeaturePlanProposal> PlanAsync(
        FeaturePlanningRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var response = await this.chatClient.CompleteAsync(
            FeaturePlanPrompt.Create(request.FeatureRequest), cancellationToken)
            .ConfigureAwait(false);
        return FeaturePlanJsonParser.Parse(response);
    }
}
