using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Tests;

public sealed class ToolProposalArtifactTests
{
    [Fact]
    public void Constructor_Should_Snapshot_The_Complete_Review_Artifact()
    {
        var capabilityId = Guid.NewGuid();
        var observationId = Guid.NewGuid();
        var tags = new List<string> { "review" };
        var sources = new Dictionary<string, string> { ["ReviewTool.cs"] = "source v1" };
        var tests = new Dictionary<string, string> { ["ReviewToolTests.cs"] = "tests v1" };
        using var parameters = JsonDocument.Parse("""{"type":"object"}""");
        var schema = new CapabilityToolSchema(
            capabilityId, "review-tool", "Review a bounded artifact.", parameters.RootElement);

        var artifact = new ToolProposalArtifact(
            schema, tags, sources, tests, "An observation showed repeated review defects.",
            [observationId], ToolExecutionProfile.ReadOnly);
        tags[0] = "changed";
        sources["ReviewTool.cs"] = "changed";
        tests["ReviewToolTests.cs"] = "changed";

        Assert.Equal(
            (capabilityId, "review", "source v1", "tests v1", observationId,
                ToolExecutionProfile.ReadOnly),
            (artifact.Schema.CapabilityId, artifact.Tags[0], artifact.SourceFiles["ReviewTool.cs"],
                artifact.TestFiles["ReviewToolTests.cs"], artifact.ObservationIds[0],
                artifact.ExecutionProfile));
    }

    [Fact]
    public void Constructor_Should_Reject_Source_Outside_The_Fixed_CSharp_Envelope()
    {
        var sources = new Dictionary<string, string> { ["../Build.csproj"] = "project" };

        Assert.Throws<ArgumentException>(() => CreateArtifact(sourceFiles: sources));
    }

    [Fact]
    public void Constructor_Should_Bound_Source_By_Strict_Utf8_Bytes()
    {
        var sources = new Dictionary<string, string>
        {
            ["Oversized.cs"] = new string('é', 524_289),
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => CreateArtifact(sourceFiles: sources));
    }

    [Fact]
    public void Constructor_Should_Bound_Review_Paths()
    {
        var sources = new Dictionary<string, string>
        {
            [$"{new string('A', 238)}.cs"] = "source",
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => CreateArtifact(sourceFiles: sources));
    }

    [Fact]
    public void Constructor_Should_Bound_Rationale_By_Strict_Utf8_Bytes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateArtifact(rationale: new string('é', 32_769)));
    }

    [Fact]
    public void Constructor_Should_Bound_The_Number_Of_Source_Files()
    {
        var sources = new Dictionary<string, string>();
        for (var index = 0; index < 65; index++)
        {
            sources.Add($"Tool{index}.cs", "source");
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => CreateArtifact(sourceFiles: sources));
    }

    [Fact]
    public void Constructor_Should_Bound_Motivating_Observations()
    {
        Guid[] observations = Enumerable.Range(0, 65).Select(_ => Guid.NewGuid()).ToArray();

        Assert.Throws<ArgumentOutOfRangeException>(() => CreateArtifact(observations: observations));
    }

    [Fact]
    public void Constructor_Should_Bound_Retrieval_Tag_Count()
    {
        string[] tags = Enumerable.Range(0, 33).Select(index => $"tag-{index}").ToArray();

        Assert.Throws<ArgumentOutOfRangeException>(() => CreateArtifact(tags: tags));
    }

    [Fact]
    public void Constructor_Should_Bound_Retrieval_Tags_By_Strict_Utf8_Bytes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateArtifact(tags: [new string('é', 129)]));
    }

    [Fact]
    public void Constructor_Should_Bound_The_Typed_Parameter_Schema()
    {
        string json = JsonSerializer.Serialize(new
        {
            type = "object",
            description = new string('x', 65_536),
        });

        Assert.Throws<ArgumentOutOfRangeException>(() => CreateArtifact(parametersJson: json));
    }

    [Fact]
    public void Constructor_Should_Bound_The_Schema_Description_By_Strict_Utf8_Bytes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateArtifact(description: new string('é', 2_049)));
    }

    [Fact]
    public void Compute_Should_Pin_All_Artifact_Bytes_Independent_Of_File_Insertion_Order()
    {
        var capabilityId = Guid.NewGuid();
        var observationId = Guid.NewGuid();
        var firstSources = new Dictionary<string, string>
        {
            ["A.cs"] = "a",
            ["B.cs"] = "b",
        };
        var reversedSources = new Dictionary<string, string>
        {
            ["B.cs"] = "b",
            ["A.cs"] = "a",
        };
        string first = CreateArtifact(
            firstSources, capabilityId: capabilityId, observationId: observationId).Version;
        string reordered = CreateArtifact(
            reversedSources, capabilityId: capabilityId, observationId: observationId).Version;
        string changedTest = CreateArtifact(
            firstSources, new Dictionary<string, string> { ["ToolTests.cs"] = "changed" },
            capabilityId, observationId).Version;

        Assert.Equal((first, true, true), (reordered, first == reordered, first != changedTest));
    }

    private static ToolProposalArtifact CreateArtifact(
        IReadOnlyDictionary<string, string>? sourceFiles = null,
        IReadOnlyDictionary<string, string>? testFiles = null,
        Guid? capabilityId = null,
        Guid? observationId = null,
        string rationale = "A rationale.",
        IReadOnlyList<Guid>? observations = null,
        string parametersJson = """{"type":"object"}""",
        IReadOnlyList<string>? tags = null,
        string description = "Review a bounded artifact.")
    {
        using var parameters = JsonDocument.Parse(parametersJson);
        var schema = new CapabilityToolSchema(
            capabilityId ?? Guid.NewGuid(), "review-tool", description, parameters.RootElement);
        return new ToolProposalArtifact(
            schema, tags ?? ["review"],
            sourceFiles ?? new Dictionary<string, string> { ["ReviewTool.cs"] = "source" },
            testFiles ?? new Dictionary<string, string> { ["ReviewToolTests.cs"] = "tests" },
            rationale, observations ?? [observationId ?? Guid.NewGuid()],
            ToolExecutionProfile.ReadOnly);
    }
}
