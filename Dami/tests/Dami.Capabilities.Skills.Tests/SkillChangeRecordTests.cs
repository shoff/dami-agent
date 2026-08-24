using Dami.Contracts.Capabilities;
using Dami.Contracts.Events;

namespace Dami.Capabilities.Skills.Tests;

public sealed class SkillChangeRecordTests
{
    [Fact]
    public void Constructor_Should_Reject_A_Diff_Whose_Utf8_Encoding_Exceeds_The_Limit()
    {
        var request = CreateAuthorRequest();
        string oversized = new('\u00e9', 524_289);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SkillChangeRecord(request, oversized, new string('a', 64), DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Constructor_Should_Reject_A_Noncanonical_Replacement_Version()
    {
        var request = CreateAuthorRequest();

        Assert.Throws<ArgumentException>(() =>
            new SkillChangeRecord(request, "+ # Compare", "version", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Constructor_Should_Reject_A_Diff_With_Invalid_Unicode()
    {
        var request = CreateAuthorRequest();
        string invalid = new('\ud800', 1);

        Assert.Throws<ArgumentException>(() =>
            new SkillChangeRecord(request, invalid, new string('a', 64), DateTimeOffset.UnixEpoch));
    }

    private static SkillChangeRequest CreateAuthorRequest()
    {
        var document = new SkillDocument(
            Guid.NewGuid(), "compare-images", "Compare images.", "# Compare",
            ["vision"], [], new Dictionary<string, string>());
        return new SkillChangeRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, ExecutionOrigin.SelfAudit,
            SkillChangeKind.Author, document.SkillId, null, document);
    }
}
