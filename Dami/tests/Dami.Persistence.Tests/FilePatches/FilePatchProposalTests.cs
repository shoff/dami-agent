using Dami.Contracts.FilePatches;
using Xunit;

namespace Dami.Persistence.Tests.FilePatches;

public sealed class FilePatchProposalTests
{
    [Fact]
    public void Constructor_Should_Reject_Replacement_Bytes_That_Do_Not_Match_The_Hash()
    {
        var exception = Assert.Throws<ArgumentException>(() => new FilePatchProposal(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "notes.txt",
            "replacement", new string('a', 64), null,
            new DateTimeOffset(2026, 8, 24, 2, 0, 0, TimeSpan.Zero)));

        Assert.Equal("replacementSha256", exception.ParamName);
    }

    [Fact]
    public void Constructor_Should_Reject_Nul_That_Postgres_Text_Cannot_Store()
    {
        const string content = "before\0after";

        var exception = Assert.Throws<ArgumentException>(() => new FilePatchProposal(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "notes.txt",
            content, FilePatchProposal.HashOf(content), null,
            new DateTimeOffset(2026, 8, 24, 2, 0, 0, TimeSpan.Zero)));

        Assert.Equal("replacementContent", exception.ParamName);
    }

    [Fact]
    public void Ddl_Should_Explicitly_Revoke_App_Mutation_Privileges()
    {
        var ddl = TestDdl.EventStoreForSchema("privilege_probe");

        Assert.Contains(
            "revoke update, delete, truncate, references, trigger\n"
            + "on privilege_probe.file_patch_proposals from dami_app;",
            ddl,
            StringComparison.Ordinal);
    }
}
