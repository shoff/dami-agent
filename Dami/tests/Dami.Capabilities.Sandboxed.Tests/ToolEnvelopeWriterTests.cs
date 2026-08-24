using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Sandboxed.Tests;

public sealed class ToolEnvelopeWriterTests : IDisposable
{
    private readonly string scratch = Path.Combine(
        Path.GetTempPath(), "dami-tool-envelope-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(this.scratch))
        {
            Directory.Delete(this.scratch, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAsync_Should_Create_Only_The_Fixed_Package_Free_Envelope()
    {
        ToolProposalArtifact artifact = CreateArtifact();
        var writer = new ToolEnvelopeWriter();

        await writer.WriteAsync(artifact, this.scratch, CancellationToken.None);

        string[] paths = Directory.GetFiles(this.scratch, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(this.scratch, path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            ["DamiSandboxContracts.cs", "NuGet.Config", "Program.cs",
                "Proposal/Source/EchoTool.cs", "Proposal/Tests/EchoToolTests.cs",
                "Tool.csproj"],
            paths);
        Assert.Contains("<clear />", await File.ReadAllTextAsync(
            Path.Combine(this.scratch, "NuGet.Config"), CancellationToken.None),
            StringComparison.Ordinal);
        Assert.DoesNotContain("PackageReference", await File.ReadAllTextAsync(
            Path.Combine(this.scratch, "Tool.csproj"), CancellationToken.None),
            StringComparison.Ordinal);
    }

    private static ToolProposalArtifact CreateArtifact()
    {
        using var parameters = JsonDocument.Parse("""{"type":"object"}""");
        var schema = new CapabilityToolSchema(
            Guid.NewGuid(), "echo", "Echo input.", parameters.RootElement);
        return new ToolProposalArtifact(
            schema, ["echo"],
            new Dictionary<string, string> { ["EchoTool.cs"] = "source" },
            new Dictionary<string, string> { ["EchoToolTests.cs"] = "tests" },
            "Existing tools cannot perform the pure transform.", [Guid.NewGuid()],
            ToolExecutionProfile.PureComputation);
    }
}
