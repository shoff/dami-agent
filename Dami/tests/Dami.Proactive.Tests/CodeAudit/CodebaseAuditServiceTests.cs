using Dami.Contracts.Models;
using Dami.Contracts.Proactive;
using Dami.Proactive.CodeAudit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.CodeAudit;

/// <summary>D-016 discipline: read-only, one finding at most, quiet by default.</summary>
public sealed class CodebaseAuditServiceTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 24, 1, 0, 0, TimeSpan.Zero);

    private readonly IGitLog gitLog = Substitute.For<IGitLog>();
    private readonly IChatClient chatClient = Substitute.For<IChatClient>();

    [Fact]
    public async Task RunPassAsync_Should_Be_Quiet_When_Nothing_Changed()
    {
        this.Arrange(patch: "", review: "irrelevant");

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Surfacings);
    }

    [Fact]
    public async Task RunPassAsync_Should_Not_Consult_The_Model_When_Nothing_Changed()
    {
        this.Arrange(patch: "", review: "irrelevant");

        await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        await this.chatClient.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default);
    }

    [Fact]
    public async Task RunPassAsync_Should_Be_Quiet_When_The_Review_Finds_Nothing()
    {
        this.Arrange(patch: "diff --git a/b", review: "NONE");

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Surfacings);
    }

    [Fact]
    public async Task RunPassAsync_Should_Surface_Exactly_One_Finding()
    {
        this.Arrange(
            patch: "diff --git a/b",
            review: "Null check missing in Foo.cs\nSuggested fix: guard the parameter.");

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Single(result.Surfacings);
    }

    [Fact]
    public async Task RunPassAsync_Should_Title_The_Surfacing_With_The_First_Line()
    {
        this.Arrange(
            patch: "diff --git a/b",
            review: "Null check missing in Foo.cs\nSuggested fix: guard the parameter.");

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal("Null check missing in Foo.cs", result.Surfacings[0].Title);
    }

    [Fact]
    public async Task RunPassAsync_Should_Truncate_A_Huge_Patch_Before_Review()
    {
        string? sent = null;
        this.gitLog.RecentPatchAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new string('x', 100_000));
        this.chatClient.CompleteAsync(Arg.Do<string>(text => sent = text), Arg.Any<CancellationToken>())
            .Returns("NONE");

        await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.True(sent!.Length < 20_000);
    }

    private void Arrange(string patch, string review)
    {
        this.gitLog.RecentPatchAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(patch);
        this.chatClient.CompleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(review);
    }

    private CodebaseAuditService CreateService()
    {
        return new CodebaseAuditService(
            this.gitLog, this.chatClient, Options.Create(new CodebaseAuditOptions()),
            new FakeTimeProvider(now), NullLogger<CodebaseAuditService>.Instance);
    }

    private static ProactiveContext Context()
    {
        return new ProactiveContext(Guid.NewGuid(), now, null);
    }
}
