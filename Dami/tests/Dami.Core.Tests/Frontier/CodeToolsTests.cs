using Dami.Contracts.Code;
using Dami.Contracts.Privacy;
using Dami.Core.Frontier;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Frontier;

public sealed class CodeToolsTests
{
    private readonly ICodeWorker worker = Substitute.For<ICodeWorker>();

    public CodeToolsTests()
    {
        this.worker.Enabled.Returns(true);
    }

    private CodeTools Create() => new(this.worker, NullLogger<CodeTools>.Instance);

    private static CodeChange Change(bool changed = true) => new(
        "dami/20260916-1830-fix-the-typo", "/home/steve/.local/share/dami/code/20260916-1830-fix-the-typo",
        changed, changed ? "2 files changed, 4 insertions(+), 1 deletion(-)" : string.Empty,
        changed, "dotnet build succeeded (exit 0)", "Fixed the typo in the About window.");

    [Fact]
    public void Should_Reject_A_Null_Worker()
    {
        Assert.Throws<ArgumentNullException>(() => new CodeTools(null!, NullLogger<CodeTools>.Instance));
    }

    [Fact]
    public void Tools_Should_Be_Hidden_When_Code_Work_Is_Off()
    {
        // Off means not offered, not "offered and refused": a schema the model cannot use
        // still costs tokens on every turn.
        this.worker.Enabled.Returns(false);

        Assert.Empty(this.Create().Tools);
    }

    [Fact]
    public void Tools_Should_Offer_Change_Explain_And_List_When_On()
    {
        var names = this.Create().Tools.Select(tool => tool.Name).ToList();

        Assert.Equal(
            [CodeTools.CHANGE_CODE, CodeTools.EXPLAIN_CODE, CodeTools.LIST_CODE_CHANGES], names);
    }

    [Fact]
    public async Task ChangeAsync_Should_Hand_The_Task_To_The_Worker()
    {
        this.worker.ChangeAsync(Arg.Any<CodeChangeRequest>(), Arg.Any<CancellationToken>()).Returns(Change());

        await this.Create().ChangeAsync(Guid.NewGuid(), "fix the typo", CancellationToken.None);

        await this.worker.Received().ChangeAsync(
            Arg.Is<CodeChangeRequest>(request => request.Task == "fix the typo"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangeAsync_Should_Report_The_Branch()
    {
        this.worker.ChangeAsync(Arg.Any<CodeChangeRequest>(), Arg.Any<CancellationToken>()).Returns(Change());

        var result = await this.Create().ChangeAsync(Guid.NewGuid(), "fix the typo", CancellationToken.None);

        Assert.Contains("dami/20260916-1830-fix-the-typo", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangeAsync_Should_Report_The_Diffstat_And_The_Build()
    {
        // The receipt: what changed and whether it builds, from this host, not the agent's word.
        this.worker.ChangeAsync(Arg.Any<CodeChangeRequest>(), Arg.Any<CancellationToken>()).Returns(Change());

        var result = await this.Create().ChangeAsync(Guid.NewGuid(), "fix the typo", CancellationToken.None);

        Assert.Contains("2 files changed", result.Text, StringComparison.Ordinal);
        Assert.Contains("dotnet build succeeded", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangeAsync_Should_Say_That_Nothing_Was_Merged()
    {
        // The model must not tell Steve the change is live. Its authority ends at the branch.
        this.worker.ChangeAsync(Arg.Any<CodeChangeRequest>(), Arg.Any<CancellationToken>()).Returns(Change());

        var result = await this.Create().ChangeAsync(Guid.NewGuid(), "fix the typo", CancellationToken.None);

        Assert.Contains("not merged", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangeAsync_Should_Say_When_No_File_Changed()
    {
        this.worker.ChangeAsync(Arg.Any<CodeChangeRequest>(), Arg.Any<CancellationToken>()).Returns(Change(changed: false));

        var result = await this.Create().ChangeAsync(Guid.NewGuid(), "fix the typo", CancellationToken.None);

        Assert.Contains("did not change any file", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangeAsync_Should_Turn_A_Refusal_Into_A_Failed_Result()
    {
        // The turn is waiting; a refusal is words for the model, not an exception for the gateway.
        this.worker.ChangeAsync(Arg.Any<CodeChangeRequest>(), Arg.Any<CancellationToken>())
            .Returns<CodeChange>(_ => throw new EgressRefusedException("the code work budget is spent"));

        var result = await this.Create().ChangeAsync(Guid.NewGuid(), "fix the typo", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("the code work budget is spent", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplainAsync_Should_Return_The_Answer()
    {
        this.worker.ExplainAsync(Arg.Any<CodeQuestion>(), Arg.Any<CancellationToken>())
            .Returns("The Gallery keeps an index in Postgres.");

        var result = await this.Create().ExplainAsync(Guid.NewGuid(), "how does the gallery work", CancellationToken.None);

        Assert.Equal("The Gallery keeps an index in Postgres.", result.Text);
    }

    [Fact]
    public async Task ListAsync_Should_Say_When_There_Are_No_Branches()
    {
        this.worker.ListChangesAsync(Arg.Any<CancellationToken>()).Returns([]);

        var result = await this.Create().ListAsync(CancellationToken.None);

        Assert.Contains("No code changes", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListAsync_Should_List_Each_Branch_With_Its_Subject_And_Diffstat()
    {
        this.worker.ListChangesAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new CodeBranch("dami/20260916-1830-fix-the-typo", new DateTimeOffset(2026, 9, 16, 18, 31, 0, TimeSpan.Zero),
                "fix the typo", "1 file changed, 1 insertion(+), 1 deletion(-)"),
        ]);

        var result = await this.Create().ListAsync(CancellationToken.None);

        Assert.Contains(
            "dami/20260916-1830-fix-the-typo | 2026-09-16 18:31 | fix the typo | 1 file changed, 1 insertion(+), 1 deletion(-)",
            result.Text, StringComparison.Ordinal);
    }
}
