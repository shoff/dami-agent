using System.Text.Json;
using Dami.Contracts.Models;
using Dami.Core.Frontier;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Dami.Host.Discord.Tests;

public sealed class DiscordToolboxTests
{
    private readonly IImageGenerator images = Substitute.For<IImageGenerator>();
    private readonly IDiscordPortraitGenerator portraits = Substitute.For<IDiscordPortraitGenerator>();
    private readonly IFrontierRecall recall = Substitute.For<IFrontierRecall>();

    public DiscordToolboxTests()
    {
        this.recall.Tool.Returns(new FrontierTool(
            FrontierRecallTool.NAME, "look it up", JsonDocument.Parse("""{"type":"object"}""").RootElement));
    }

    private static FrontierToolCall Call(string tool, string json)
    {
        using var document = JsonDocument.Parse(json);
        return new FrontierToolCall("call-1", tool, document.RootElement.Clone());
    }

    private DiscordToolbox.DiscordTurnTools Tools() =>
        new DiscordToolbox(this.images, this.portraits, this.recall, NullLogger<DiscordToolbox>.Instance)
            .ForTurn(Guid.NewGuid());

    [Fact]
    public void The_Bundle_Should_Be_Two_Picture_Tools_And_Recall()
    {
        var tools = this.Tools().Toolbox.Tools;

        Assert.Equal(
            [DiscordToolbox.MAKE_PORTRAIT, DiscordToolbox.MAKE_IMAGE, FrontierRecallTool.NAME],
            tools.Select(tool => tool.Name));
        Assert.All(tools, tool => Assert.Equal("object", tool.InputSchema.GetProperty("type").GetString()));
    }

    [Fact]
    public async Task Recall_Should_Go_Through_The_Gated_Tool_With_The_Turns_Trace()
    {
        this.recall.RecallAsync(Arg.Any<Guid>(), "monday lifts", Arg.Any<CancellationToken>())
            .Returns(FrontierToolResult.Ok("- 225 for five"));
        var tools = this.Tools();

        var result = await tools.HandleAsync(
            Call(FrontierRecallTool.NAME, """{"query":"monday lifts"}"""), CancellationToken.None);

        Assert.Equal("- 225 for five", result.Text);
        Assert.Empty(tools.Attachments);
    }

    [Fact]
    public async Task Make_Portrait_Should_Draw_Dami_And_Keep_The_Attachment()
    {
        this.portraits.GenerateAsync("on the porch", Arg.Any<CancellationToken>())
            .Returns(new GeneratedImage("dami-9.png", new ReadOnlyMemory<byte>([1]), "image/png", "p"));
        var tools = this.Tools();

        var result = await tools.HandleAsync(
            Call(DiscordToolbox.MAKE_PORTRAIT, """{"scene":"on the porch"}"""), CancellationToken.None);

        Assert.True(result.Success);
        Assert.DoesNotContain("/", result.Text, StringComparison.Ordinal);
        Assert.Equal("dami-9.png", Assert.Single(tools.Attachments).FileName);
    }

    [Fact]
    public async Task Make_Image_Should_Send_An_Egressable_Request_With_The_Prompt()
    {
        this.images.GenerateAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GeneratedImage("barn.png", new ReadOnlyMemory<byte>([1]), "image/png", "p"));
        var tools = this.Tools();

        var result = await tools.HandleAsync(
            Call(DiscordToolbox.MAKE_IMAGE, """{"prompt":"a red barn"}"""), CancellationToken.None);

        Assert.True(result.Success);
        await this.images.Received(1).GenerateAsync(
            Arg.Is<ImageRequest>(request => request.Prompt == "a red barn"
                && request.Privacy == Dami.Contracts.Context.PrivacyClass.Egressable),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_Unknown_Tool_Should_Fail_In_Words_Not_Exceptions()
    {
        var result = await this.Tools().HandleAsync(Call("send_email", "{}"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("send_email", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Generator_Failure_Should_Become_A_Failed_Result_The_Turn_Can_Continue_On()
    {
        // The app-server is blocked until the call is answered; an exception here would
        // be a hung turn, then the ten-minute deadline.
        this.portraits.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<GeneratedImage>>(_ => throw new InvalidOperationException("codex image tool refused"));
        var tools = this.Tools();

        var result = await tools.HandleAsync(
            Call(DiscordToolbox.MAKE_PORTRAIT, """{"scene":"x"}"""), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("codex image tool refused", result.Text, StringComparison.Ordinal);
        Assert.Empty(tools.Attachments);
    }

    [Fact]
    public async Task A_Missing_Argument_Should_Fail_Without_Drawing()
    {
        var result = await this.Tools().HandleAsync(
            Call(DiscordToolbox.MAKE_PORTRAIT, """{"scene":""}"""), CancellationToken.None);

        Assert.False(result.Success);
        await this.portraits.DidNotReceive().GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
