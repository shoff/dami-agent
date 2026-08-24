using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dami.Contracts.Capabilities;
using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Native;

/// <summary>Stages inert self-authored source and tests for human review.</summary>
[NativeCapability(
    "888d13a7-74a0-49da-8da5-eaec191c634d",
    "propose-tool",
    "Stage bounded C# source and tests for human review; never compile or register them.",
    "native://propose-tool/schema/v1",
    "1.0.0",
    ParametersJson = """
        {"type":"object","properties":{"capabilityId":{"type":"string","format":"uuid"},"name":{"type":"string"},"description":{"type":"string"},"parameters":{"type":"object"},"tags":{"type":"array","items":{"type":"string"}},"sourceFiles":{"type":"object","additionalProperties":{"type":"string"}},"testFiles":{"type":"object","additionalProperties":{"type":"string"}},"rationale":{"type":"string"},"observationIds":{"type":"array","items":{"type":"string","format":"uuid"}},"executionProfile":{"type":"string","enum":["PureComputation","ReadOnly"]}},"required":["capabilityId","name","description","parameters","tags","sourceFiles","testFiles","rationale","observationIds","executionProfile"],"additionalProperties":false}
        """,
    Tags = new[] { "tools", "authoring", "staging", "review" })]
public sealed class ProposeToolCapabilityHandler : INativeCapabilityHandler
{
    private static readonly Guid proposalIdNamespace =
        new("04bd8fbd-6900-4164-8f95-99790ce23f07");
    private static readonly Guid spanIdNamespace =
        new("ee625d0b-6b0d-469e-9c44-5d902d16fc3b");
    private static readonly JsonSerializerOptions serializerOptions = CreateSerializerOptions();

    private readonly TimeProvider clock;
    private readonly IToolProposalStore store;

    /// <summary>Creates the propose-only native handler.</summary>
    public ProposeToolCapabilityHandler(IToolProposalStore store, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        this.store = store;
        this.clock = clock;
    }

    /// <inheritdoc />
    public async Task<CapabilityExecutionResult> ExecuteAsync(
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ToolProposalArtifact artifact = CreateArtifact(request.Invocation.Arguments);
        var proposalRequest = new ToolProposalRequest(
            NativeInvocationIdentity.Derive(
                proposalIdNamespace, request.TraceId, request.SpanId),
            request.TraceId,
            NativeInvocationIdentity.Derive(spanIdNamespace, request.TraceId, request.SpanId),
            request.SpanId,
            request.Origin,
            artifact);
        var proposal = new StagedToolProposal(
            proposalRequest, artifact.Version, this.clock.GetUtcNow());
        StagedToolProposal accepted = await this.store
            .StageAsync(proposal, cancellationToken).ConfigureAwait(false);
        return CreateResult(accepted);
    }

    private static ToolProposalArtifact CreateArtifact(JsonElement source)
    {
        ToolProposalArguments arguments = source.Deserialize<ToolProposalArguments>(serializerOptions)
            ?? throw new ArgumentException("Propose-tool arguments are required.", nameof(source));
        if (arguments.ExecutionProfile is not { } profile)
        {
            throw new ArgumentException(
                "Propose-tool requires an execution profile.", nameof(source));
        }

        var schema = new CapabilityToolSchema(
            arguments.CapabilityId,
            Require(arguments.Name, "name"),
            Require(arguments.Description, "description"),
            arguments.Parameters);
        return new ToolProposalArtifact(
            schema,
            Require(arguments.Tags, "tags"),
            Require(arguments.SourceFiles, "sourceFiles"),
            Require(arguments.TestFiles, "testFiles"),
            Require(arguments.Rationale, "rationale"),
            Require(arguments.ObservationIds, "observationIds"),
            profile);
    }

    private static T Require<T>(T? value, string propertyName)
        where T : class
    {
        return value ?? throw new ArgumentException(
            $"Propose-tool requires '{propertyName}'.", nameof(value));
    }

    private static CapabilityExecutionResult CreateResult(StagedToolProposal proposal)
    {
        var evidence = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["proposal_id"] = proposal.Request.ProposalId.ToString("D"),
            ["capability_id"] = proposal.Request.Artifact.Schema.CapabilityId.ToString("D"),
            ["artifact_version"] = proposal.ArtifactVersion,
            ["registered"] = "false",
            ["executed"] = "false",
        };
        string output = string.Create(
            CultureInfo.InvariantCulture,
            $"Tool proposal '{proposal.Request.ProposalId:D}' version '{proposal.ArtifactVersion}' staged for human review; not registered or executed.");
        return new CapabilityExecutionResult(output, evidence);
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }

    private sealed record ToolProposalArguments(
        Guid CapabilityId,
        string? Name,
        string? Description,
        JsonElement Parameters,
        IReadOnlyList<string>? Tags,
        IReadOnlyDictionary<string, string>? SourceFiles,
        IReadOnlyDictionary<string, string>? TestFiles,
        string? Rationale,
        IReadOnlyList<Guid>? ObservationIds,
        ToolExecutionProfile? ExecutionProfile);
}
