using Dami.Contracts.Capabilities;
using Dami.Core.Turns;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Turns;

public sealed class SkillPromptBuilderTests
{
    [Fact]
    public async Task BuildAsync_Should_Disclose_Only_The_Selected_Skill_Body()
    {
        var selected = new SkillSelection(
            Guid.NewGuid(), "image-comparison", "skill://body", "version-1");
        var reader = Substitute.For<ISkillContentReader>();
        reader.ReadBodyAsync(
                selected.CapabilityId, selected.Version, Arg.Any<CancellationToken>())
            .Returns("Compare the images pixel by pixel.");
        var builder = new SkillPromptBuilder(
            reader,
            Options.Create(new SkillPromptOptions { MaxPromptCharacters = 1_000 }));

        string section = await builder.BuildAsync([selected], CancellationToken.None);

        Assert.Contains("image-comparison", section, StringComparison.Ordinal);
        Assert.Contains("Compare the images pixel by pixel.", section, StringComparison.Ordinal);
        await reader.Received(1).ReadBodyAsync(
            selected.CapabilityId, selected.Version, Arg.Any<CancellationToken>());
        await reader.DidNotReceiveWithAnyArgs().ReadReferenceAsync(
            default, default!, default!, default);
    }

    [Fact]
    public async Task BuildAsync_Should_Reject_OverBudget_Metadata_Before_Content_Io()
    {
        var selected = new SkillSelection(
            Guid.NewGuid(), "image-comparison", "skill://body", "version-1");
        var reader = Substitute.For<ISkillContentReader>();
        var builder = new SkillPromptBuilder(
            reader,
            Options.Create(new SkillPromptOptions { MaxPromptCharacters = 1 }));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => builder.BuildAsync([selected], CancellationToken.None));

        await reader.DidNotReceiveWithAnyArgs().ReadBodyAsync(default, default!, default);
    }
}
