using System.Text.Json;
using Dami.Contracts.Capabilities;

namespace Dami.Capabilities.Skills.Tests;

public sealed class SkillDocumentVersionerTests : IDisposable
{
    private static readonly DateTimeOffset registeredAt = DateTimeOffset.UnixEpoch;

    private readonly string scratch = Path.Combine(
        Path.GetTempPath(), "dami-skill-version-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Compute_Should_Match_The_Version_Published_From_The_Serialized_Document()
    {
        SkillDocument document = CreateDocument();
        await this.WriteDocumentAsync(document);
        var registry = new CapabilityRegistry();
        var loader = new SkillCapabilityLoader(
            registry, new SkillLoaderOptions { RootDirectory = this.scratch });

        string predicted = new SkillDocumentVersioner().Compute(document);
        CapabilityEntry published = (
            await loader.LoadAsync(registeredAt, CancellationToken.None)).Single();

        Assert.Equal(published.Version, predicted);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.scratch))
        {
            Directory.Delete(this.scratch, recursive: true);
        }
    }

    private static SkillDocument CreateDocument()
    {
        return new SkillDocument(
            Guid.NewGuid(), "compare-images", "Compare images.", "# Compare",
            ["vision"], [], new Dictionary<string, string>
            {
                ["z-last.md"] = "Last",
                ["a-first.md"] = "First",
            });
    }

    private async Task WriteDocumentAsync(SkillDocument document)
    {
        string directory = Directory.CreateDirectory(
            Path.Combine(this.scratch, document.SkillId.ToString("D"))).FullName;
        string[] references = document.References.Keys.Order(StringComparer.Ordinal).ToArray();
        string descriptor = JsonSerializer.Serialize(new
        {
            id = document.SkillId,
            name = document.Name,
            description = document.Description,
            tags = document.Tags,
            relatedCapabilities = document.RelatedCapabilities,
            references,
        });
        await File.WriteAllTextAsync(Path.Combine(directory, "skill.json"), descriptor);
        await File.WriteAllTextAsync(Path.Combine(directory, "SKILL.md"), document.Body);
        foreach (string reference in references)
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, reference), document.References[reference]);
        }
    }
}
