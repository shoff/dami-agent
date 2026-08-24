using Dami.Contracts.Capabilities;
using Dami.Contracts.Events;

namespace Dami.Capabilities.Skills.Tests;

public sealed class SkillChangeRequestTests
{
    [Fact]
    public void Revise_Should_Require_The_Expected_Preimage_Version()
    {
        var skillId = Guid.NewGuid();
        var replacement = new SkillDocument(
            skillId,
            "image-comparison",
            "Procedure for comparing images.",
            "# Compare images",
            ["vision"],
            [],
            new Dictionary<string, string>());

        var exception = Assert.Throws<ArgumentException>(() => new SkillChangeRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            parentSpanId: null,
            ExecutionOrigin.SelfAudit,
            SkillChangeKind.Revise,
            skillId,
            expectedVersion: null,
            replacement));

        Assert.Equal("expectedVersion", exception.ParamName);
    }

    [Fact]
    public void Revise_Should_Reject_A_Noncanonical_Preimage_Version()
    {
        SkillDocument document = CreateDocument();

        Assert.Throws<ArgumentException>(() => CreateRequest(
            SkillChangeKind.Revise, document.SkillId, "version", document));
    }

    [Fact]
    public void Author_And_Retire_Should_Enforce_Their_Document_Shapes()
    {
        SkillDocument document = CreateDocument();

        SkillChangeRequest author = CreateRequest(
            SkillChangeKind.Author, document.SkillId, expectedVersion: null, document);
        SkillChangeRequest retire = CreateRequest(
            SkillChangeKind.Retire, document.SkillId, new string('a', 64), replacement: null);

        Assert.Same(document, author.Replacement);
        Assert.Null(retire.Replacement);
        Assert.Throws<ArgumentException>(() => CreateRequest(
            SkillChangeKind.Author, document.SkillId, "unexpected", document));
        Assert.Throws<ArgumentException>(() => CreateRequest(
            SkillChangeKind.Retire, document.SkillId, new string('a', 64), document));
    }

    private static SkillDocument CreateDocument()
    {
        return new SkillDocument(
            Guid.NewGuid(),
            "image-comparison",
            "Procedure for comparing images.",
            "# Compare images",
            ["vision"],
            [],
            new Dictionary<string, string>());
    }

    private static SkillChangeRequest CreateRequest(
        SkillChangeKind kind,
        Guid skillId,
        string? expectedVersion,
        SkillDocument? replacement)
    {
        return new SkillChangeRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), parentSpanId: null,
            ExecutionOrigin.SelfAudit, kind, skillId, expectedVersion, replacement);
    }
}
