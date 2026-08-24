using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Events;

namespace Dami.Capabilities.Skills.Tests;

public sealed class SkillChangeMaterializerTests : IDisposable
{
    private static readonly DateTimeOffset at = DateTimeOffset.UnixEpoch;

    private readonly string scratch = Path.Combine(
        Path.GetTempPath(), "dami-skill-materializer-" + Guid.NewGuid().ToString("N"));
    private readonly string outside = Path.Combine(
        Path.GetTempPath(), "dami-skill-materializer-outside-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ApplyAsync_Should_Author_The_Exact_Publishable_Document()
    {
        Directory.CreateDirectory(this.scratch);
        SkillChangeRecord record = CreateAuthorRecord();
        var materializer = new SkillChangeMaterializer(
            new SkillLoaderOptions { RootDirectory = this.scratch },
            new SkillDocumentVersioner());

        await materializer.ApplyAsync(record, CancellationToken.None);

        var registry = new CapabilityRegistry();
        var loader = new SkillCapabilityLoader(
            registry, new SkillLoaderOptions { RootDirectory = this.scratch });
        CapabilityEntry published = (
            await loader.LoadAsync(at, CancellationToken.None)).Single();
        string body = await loader.ReadBodyAsync(
            published.CapabilityId, published.Version, CancellationToken.None);
        int stagingDirectories = Directory.GetDirectories(this.scratch, ".dami-*", SearchOption.TopDirectoryOnly).Length;

        Assert.Equal(
            (record.ReplacementVersion, record.Request.Replacement!.Body, 0),
            (published.Version, body, stagingDirectories));
    }

    [Fact]
    public async Task ApplyAsync_Should_Converge_An_Author_Retry_After_The_Move()
    {
        Directory.CreateDirectory(this.scratch);
        SkillChangeRecord record = CreateAuthorRecord();
        var materializer = new SkillChangeMaterializer(
            new SkillLoaderOptions { RootDirectory = this.scratch },
            new SkillDocumentVersioner());

        await materializer.ApplyAsync(record, CancellationToken.None);
        await materializer.ApplyAsync(record, CancellationToken.None);

        Assert.Single(Directory.GetDirectories(
            this.scratch, "*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task ApplyAsync_Should_Repair_Corrupted_Content_Owned_By_The_Author_Change()
    {
        Directory.CreateDirectory(this.scratch);
        SkillChangeRecord record = CreateAuthorRecord();
        var options = new SkillLoaderOptions { RootDirectory = this.scratch };
        var materializer = new SkillChangeMaterializer(options, new SkillDocumentVersioner());
        await materializer.ApplyAsync(record, CancellationToken.None);
        string directory = Directory.GetDirectories(this.scratch).Single();
        await File.WriteAllTextAsync(Path.Combine(directory, "SKILL.md"), "corrupted");

        await materializer.ApplyAsync(record, CancellationToken.None);

        Assert.Equal(
            record.Request.Replacement!.Body,
            await File.ReadAllTextAsync(Path.Combine(directory, "SKILL.md")));
    }

    [Fact]
    public async Task ApplyAsync_Should_Atomically_Revise_The_Expected_Preimage()
    {
        Directory.CreateDirectory(this.scratch);
        SkillChangeRecord authored = CreateAuthorRecord();
        SkillChangeRecord revised = CreateReviseRecord(authored);
        var options = new SkillLoaderOptions { RootDirectory = this.scratch };
        var materializer = new SkillChangeMaterializer(options, new SkillDocumentVersioner());
        await materializer.ApplyAsync(authored, CancellationToken.None);

        await materializer.ApplyAsync(revised, CancellationToken.None);

        var loader = new SkillCapabilityLoader(new CapabilityRegistry(), options);
        CapabilityEntry published = (
            await loader.LoadAsync(at, CancellationToken.None)).Single();
        string body = await loader.ReadBodyAsync(
            published.CapabilityId, published.Version, CancellationToken.None);
        Assert.Equal(
            (revised.ReplacementVersion, "# Revised", 1),
            (published.Version, body, Directory.GetDirectories(this.scratch).Length));
    }

    [Fact]
    public async Task ApplyAsync_Should_Repair_Corrupted_Content_Owned_By_The_Revision()
    {
        Directory.CreateDirectory(this.scratch);
        SkillChangeRecord authored = CreateAuthorRecord();
        SkillChangeRecord revised = CreateReviseRecord(authored);
        var options = new SkillLoaderOptions { RootDirectory = this.scratch };
        var materializer = new SkillChangeMaterializer(options, new SkillDocumentVersioner());
        await materializer.ApplyAsync(authored, CancellationToken.None);
        await materializer.ApplyAsync(revised, CancellationToken.None);
        string directory = Directory.GetDirectories(this.scratch).Single();
        await File.WriteAllTextAsync(Path.Combine(directory, "SKILL.md"), "corrupted");

        await materializer.ApplyAsync(revised, CancellationToken.None);

        Assert.Equal(
            revised.Request.Replacement!.Body,
            await File.ReadAllTextAsync(Path.Combine(directory, "SKILL.md")));
    }

    [Fact]
    public async Task ApplyAsync_Should_Retire_The_Expected_Preimage()
    {
        Directory.CreateDirectory(this.scratch);
        SkillChangeRecord authored = CreateAuthorRecord();
        SkillChangeRecord retired = CreateRetireRecord(authored);
        var options = new SkillLoaderOptions { RootDirectory = this.scratch };
        var materializer = new SkillChangeMaterializer(options, new SkillDocumentVersioner());
        await materializer.ApplyAsync(authored, CancellationToken.None);

        await materializer.ApplyAsync(retired, CancellationToken.None);

        var loader = new SkillCapabilityLoader(new CapabilityRegistry(), options);
        IReadOnlyList<CapabilityEntry> published = await loader.LoadAsync(
            at, CancellationToken.None);
        Assert.Equal((0, 0), (published.Count, Directory.GetDirectories(this.scratch).Length));
    }

    [Fact]
    public async Task ApplyAsync_Should_Recover_Retirement_After_The_Target_Move()
    {
        Directory.CreateDirectory(this.scratch);
        SkillChangeRecord authored = CreateAuthorRecord();
        SkillChangeRecord retired = CreateRetireRecord(authored);
        var options = new SkillLoaderOptions { RootDirectory = this.scratch };
        var materializer = new SkillChangeMaterializer(options, new SkillDocumentVersioner());
        await materializer.ApplyAsync(authored, CancellationToken.None);
        string target = Path.Combine(this.scratch, authored.Request.SkillId.ToString("D"));
        string retiredPath = Path.Combine(
            this.scratch, $".dami-retire-{retired.Request.ChangeId:N}");
        string tombstone = Path.Combine(
            this.scratch, $".dami-retirement-{retired.Request.ChangeId:N}");
        Directory.Move(target, retiredPath);
        await File.WriteAllTextAsync(tombstone, string.Empty);

        await materializer.ApplyAsync(retired, CancellationToken.None);

        Assert.Equal(
            (0, retired.Request.ExpectedVersion),
            (Directory.GetDirectories(this.scratch).Length, await File.ReadAllTextAsync(tombstone)));
    }

    [Fact]
    public async Task ApplyAsync_Should_Revise_An_Existing_Human_Named_Skill_Directory()
    {
        Directory.CreateDirectory(this.scratch);
        SkillChangeRecord authored = CreateAuthorRecord();
        await this.WriteLegacyDocumentAsync("human-name", authored.Request.Replacement!);
        SkillChangeRecord revised = CreateReviseRecord(authored);
        var options = new SkillLoaderOptions { RootDirectory = this.scratch };
        var materializer = new SkillChangeMaterializer(options, new SkillDocumentVersioner());

        await materializer.ApplyAsync(revised, CancellationToken.None);

        var loader = new SkillCapabilityLoader(new CapabilityRegistry(), options);
        CapabilityEntry published = (
            await loader.LoadAsync(at, CancellationToken.None)).Single();
        Assert.Equal(
            ("human-name", revised.ReplacementVersion),
            (Path.GetFileName(Directory.GetDirectories(this.scratch).Single()), published.Version));
    }

    [Fact]
    public async Task ApplyAsync_Should_Reject_A_Symbolic_Link_Skill_Root()
    {
        Directory.CreateDirectory(this.outside);
        Directory.CreateSymbolicLink(this.scratch, this.outside);
        SkillChangeRecord record = CreateAuthorRecord();
        var materializer = new SkillChangeMaterializer(
            new SkillLoaderOptions { RootDirectory = this.scratch },
            new SkillDocumentVersioner());

        Exception? exception = await Record.ExceptionAsync(
            () => materializer.ApplyAsync(record, CancellationToken.None));

        Assert.Equal(
            (typeof(InvalidDataException), 0),
            (exception?.GetType(), Directory.GetDirectories(this.outside).Length));
    }

    [Fact]
    public async Task ApplyAsync_Should_Refuse_An_Author_That_Exceeds_Skill_Capacity()
    {
        Directory.CreateDirectory(this.scratch);
        var options = new SkillLoaderOptions { RootDirectory = this.scratch, MaxSkills = 1 };
        var materializer = new SkillChangeMaterializer(options, new SkillDocumentVersioner());
        await materializer.ApplyAsync(CreateAuthorRecord(), CancellationToken.None);

        Exception? exception = await Record.ExceptionAsync(
            () => materializer.ApplyAsync(CreateAuthorRecord(), CancellationToken.None));

        Assert.Equal(
            (typeof(InvalidDataException), 1),
            (exception?.GetType(), Directory.GetDirectories(this.scratch).Length));
    }

    public void Dispose()
    {
        if (Directory.Exists(this.scratch))
        {
            Directory.Delete(this.scratch, recursive: true);
        }

        if (Directory.Exists(this.outside))
        {
            Directory.Delete(this.outside, recursive: true);
        }
    }

    private static SkillChangeRecord CreateAuthorRecord()
    {
        var document = new SkillDocument(
            Guid.NewGuid(), "compare-images", "Compare images.", "# Compare",
            ["vision"], [], new Dictionary<string, string> { ["example.md"] = "Example" });
        var request = new SkillChangeRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, ExecutionOrigin.SelfAudit,
            SkillChangeKind.Author, document.SkillId, null, document);
        string version = new SkillDocumentVersioner().Compute(document);
        return new SkillChangeRecord(request, "+ # Compare", version, at);
    }

    private static SkillChangeRecord CreateReviseRecord(SkillChangeRecord authored)
    {
        SkillDocument original = authored.Request.Replacement!;
        var replacement = new SkillDocument(
            original.SkillId, original.Name, original.Description, "# Revised",
            original.Tags, original.RelatedCapabilities, original.References);
        var request = new SkillChangeRequest(
            Guid.NewGuid(), authored.Request.TraceId, Guid.NewGuid(), authored.Request.SpanId,
            ExecutionOrigin.SelfAudit, SkillChangeKind.Revise, original.SkillId,
            authored.ReplacementVersion, replacement);
        string version = new SkillDocumentVersioner().Compute(replacement);
        return new SkillChangeRecord(request, "- # Compare\n+ # Revised", version, at.AddMinutes(1));
    }

    private static SkillChangeRecord CreateRetireRecord(SkillChangeRecord authored)
    {
        var request = new SkillChangeRequest(
            Guid.NewGuid(), authored.Request.TraceId, Guid.NewGuid(), authored.Request.SpanId,
            ExecutionOrigin.SelfAudit, SkillChangeKind.Retire, authored.Request.SkillId,
            authored.ReplacementVersion, replacement: null);
        return new SkillChangeRecord(
            request, "- retired skill", replacementVersion: null, at.AddMinutes(1));
    }

    private async Task WriteLegacyDocumentAsync(string name, SkillDocument document)
    {
        string directory = Directory.CreateDirectory(Path.Combine(this.scratch, name)).FullName;
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
