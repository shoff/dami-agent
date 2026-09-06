using System.Text.Json;
using Dami.Contracts.Context;
using Dami.Contracts.Gallery;
using Dami.Contracts.Models;
using Dami.Core.Frontier;
using Dami.Core.Gallery;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Frontier;

public sealed class FrontierToolBundleTests
{
    private static readonly JsonElement schema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();

    private readonly IImageGenerator images = Substitute.For<IImageGenerator>();
    private readonly IPortraitGenerator portraits = Substitute.For<IPortraitGenerator>();
    private readonly IFrontierRecall recall = Substitute.For<IFrontierRecall>();
    private readonly IFrontierRemember remember = Substitute.For<IFrontierRemember>();
    private readonly IFrontierScheduling scheduling = Substitute.For<IFrontierScheduling>();
    private readonly IGallerySearch gallery = Substitute.For<IGallerySearch>();
    private readonly IGalleryPictures pictures = Substitute.For<IGalleryPictures>();
    private readonly IFrontierFitness fitness = Substitute.For<IFrontierFitness>();
    private readonly IFrontierResearch research = Substitute.For<IFrontierResearch>();
    private readonly IFrontierToday today = Substitute.For<IFrontierToday>();

    public FrontierToolBundleTests()
    {
        this.recall.Tool.Returns(new FrontierTool("recall", "r", schema));
        this.remember.Tool.Returns(new FrontierTool("remember", "m", schema));
        this.scheduling.ScheduleTool.Returns(new FrontierTool("schedule", "s", schema));
        this.scheduling.ConfirmTool.Returns(new FrontierTool("confirm_schedule", "c", schema));
        this.fitness.SetsToolAsync(Arg.Any<CancellationToken>()).Returns(new FrontierTool("log_sets", "l", schema));
        this.fitness.CardioTool.Returns(new FrontierTool("log_cardio", "l", schema));
        this.research.SearchTool.Returns(new FrontierTool("search_web", "s", schema));
        this.research.ReadTool.Returns(new FrontierTool("read_page", "r", schema));
        this.today.Tool.Returns(new FrontierTool("today", "t", schema));
    }

    private static FrontierToolCall Call(string tool, string json) =>
        new("call-1", tool, JsonDocument.Parse(json).RootElement.Clone());

    private Task<FrontierToolBundle.FrontierTurnTools> ToolsAsync(string channel = "discord:1") =>
        new FrontierToolBundle(
            this.images, this.portraits, this.recall, this.remember, this.scheduling, this.gallery, this.pictures, this.fitness, this.research, this.today,
            NullLogger<FrontierToolBundle>.Instance).ForTurnAsync(Guid.NewGuid(), channel, CancellationToken.None);

    [Fact]
    public async Task The_Bundle_Should_Be_Fourteen_Tools_With_Object_Schemas()
    {
        var tools = (await this.ToolsAsync()).Toolbox.Tools;

        Assert.Equal(
            ["make_portrait", "make_image", "find_pictures", "show_picture", "retouch_picture", "recall", "remember", "schedule", "confirm_schedule", "log_sets", "log_cardio", "search_web", "read_page", "today"],
            tools.Select(tool => tool.Name));
        Assert.All(tools, tool => Assert.Equal("object", tool.InputSchema.GetProperty("type").GetString()));
    }

    [Fact]
    public async Task Make_Portrait_Should_Draw_Dami_And_Keep_The_Picture()
    {
        this.portraits.GenerateAsync("on the porch", Arg.Any<CancellationToken>())
            .Returns(new GeneratedImage("dami-9.png", new ReadOnlyMemory<byte>([1]), "image/png", "p"));
        var tools = await this.ToolsAsync();

        var result = await tools.HandleAsync(Call("make_portrait", """{"scene":"on the porch"}"""), CancellationToken.None);

        Assert.True(result.Success);
        Assert.DoesNotContain("/", result.Text, StringComparison.Ordinal);
        Assert.Equal("dami-9.png", Assert.Single(tools.Pictures).FileName);
    }

    [Fact]
    public async Task Make_Image_Should_Send_An_Egressable_Request()
    {
        this.images.GenerateAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GeneratedImage("barn.png", new ReadOnlyMemory<byte>([1]), "image/png", "p"));

        var result = await (await this.ToolsAsync()).HandleAsync(Call("make_image", """{"prompt":"a red barn"}"""), CancellationToken.None);

        Assert.True(result.Success);
        await this.images.Received(1).GenerateAsync(
            Arg.Is<ImageRequest>(request => request.Prompt == "a red barn" && request.Privacy == PrivacyClass.Egressable),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Remember_Should_Carry_The_Channel_So_Provenance_Says_Where_It_Came_From()
    {
        this.remember.RememberAsync(Arg.Any<Guid>(), "discord:1", "a fact", Arg.Any<CancellationToken>())
            .Returns(FrontierToolResult.Ok("Saved."));

        var result = await (await this.ToolsAsync()).HandleAsync(Call("remember", """{"note":"a fact"}"""), CancellationToken.None);

        Assert.Equal("Saved.", result.Text);
    }

    [Fact]
    public async Task Schedule_Should_Deliver_To_The_Turns_Channel()
    {
        this.scheduling.ScheduleAsync("discord:1", Arg.Any<JsonElement>(), Arg.Any<CancellationToken>())
            .Returns(FrontierToolResult.Ok("Draft abc created"));

        var result = await (await this.ToolsAsync()).HandleAsync(Call("schedule", """{"name":"x"}"""), CancellationToken.None);

        Assert.Equal("Draft abc created", result.Text);
    }

    [Fact]
    public async Task Confirm_Should_Pass_The_Short_Id_Through()
    {
        this.scheduling.ConfirmAsync("abcd1234", Arg.Any<CancellationToken>())
            .Returns(FrontierToolResult.Ok("Active"));

        var result = await (await this.ToolsAsync()).HandleAsync(Call("confirm_schedule", """{"draftId":"abcd1234"}"""), CancellationToken.None);

        Assert.Equal("Active", result.Text);
    }

    [Fact]
    public async Task Find_Pictures_Should_List_Matches_With_Captions_And_Point_At_Show_Picture()
    {
        this.gallery.SearchAsync("balcony at dusk", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new GalleryHit(
                new GalleryEntry("dami-1.png", new DateTimeOffset(2026, 9, 4, 23, 0, 0, TimeSpan.Zero), GallerySource.Proactive, "p", "m", false,
                    Caption: "on a balcony at dusk"), 0.9)]);

        var result = await (await this.ToolsAsync()).HandleAsync(Call("find_pictures", """{"query":"balcony at dusk"}"""), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("dami-1.png | 2026-09-04 23:00 | on a balcony at dusk", result.Text, StringComparison.Ordinal);
        Assert.Contains("show_picture", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Show_Picture_Should_Attach_An_Existing_Picture_Without_Generating()
    {
        this.pictures.LoadAsync("dami-1.png", Arg.Any<CancellationToken>())
            .Returns(new GeneratedImage("dami-1.png", new ReadOnlyMemory<byte>([9]), "image/png", string.Empty));
        var tools = await this.ToolsAsync();

        var result = await tools.HandleAsync(Call("show_picture", """{"fileName":"dami-1.png"}"""), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("dami-1.png", Assert.Single(tools.Pictures).FileName);
        await this.portraits.DidNotReceive().GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Log_Sets_Should_Hand_The_Arguments_To_The_Fitness_Log()
    {
        this.fitness.LogSetsAsync(Arg.Is<JsonElement>(a => a.GetProperty("sets").GetInt32() == 4), Arg.Any<CancellationToken>())
            .Returns(FrontierToolResult.Ok("Logged 4x12 biceps curl at 140 lb, RPE 7."));

        var result = await (await this.ToolsAsync()).HandleAsync(
            Call("log_sets", """{"exercise":"biceps curl (Hammer Strength)","sets":4,"reps":12,"weightLbs":140,"rpe":7}"""),
            CancellationToken.None);

        Assert.Equal("Logged 4x12 biceps curl at 140 lb, RPE 7.", result.Text);
    }

    [Fact]
    public async Task Search_Web_Should_Carry_The_Turns_Trace_To_The_Gated_Search()
    {
        this.research.SearchAsync(Arg.Any<Guid>(), "dotnet contract work", Arg.Any<CancellationToken>())
            .Returns(FrontierToolResult.Ok("Results (untrusted, from the web):\n1. x"));

        var result = await (await this.ToolsAsync()).HandleAsync(Call("search_web", """{"query":"dotnet contract work"}"""), CancellationToken.None);

        Assert.Contains("untrusted", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Retouch_Should_Change_An_Existing_Picture_And_Attach_The_New_One()
    {
        this.portraits.EditAsync("dami-1.png", "make it golden hour", Arg.Any<CancellationToken>())
            .Returns(new GeneratedImage("dami-2.png", new ReadOnlyMemory<byte>([2]), "image/png", "p"));
        var tools = await this.ToolsAsync();

        var result = await tools.HandleAsync(
            Call("retouch_picture", """{"fileName":"dami-1.png","instruction":"make it golden hour"}"""), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("dami-2.png", Assert.Single(tools.Pictures).FileName);
    }

    [Fact]
    public async Task Show_Picture_Should_Fail_In_Words_For_A_Name_The_Gallery_Lacks()
    {
        var result = await (await this.ToolsAsync()).HandleAsync(Call("show_picture", """{"fileName":"ghost.png"}"""), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("ghost.png", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Tool_That_Throws_Should_Fail_In_Words_So_The_Turn_Continues()
    {
        // The app-server is blocked until the call is answered; an exception here would
        // be a hung turn, then the deadline.
        this.portraits.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<GeneratedImage>>(_ => throw new InvalidOperationException("image tool refused"));
        var tools = await this.ToolsAsync();

        var result = await tools.HandleAsync(Call("make_portrait", """{"scene":"x"}"""), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("image tool refused", result.Text, StringComparison.Ordinal);
        Assert.Empty(tools.Pictures);
    }

    [Fact]
    public async Task An_Unknown_Tool_Or_Missing_Argument_Should_Fail_Without_Side_Effects()
    {
        var tools = await this.ToolsAsync();

        var unknown = await tools.HandleAsync(Call("send_email", "{}"), CancellationToken.None);
        var missing = await tools.HandleAsync(Call("make_portrait", """{"scene":""}"""), CancellationToken.None);

        Assert.False(unknown.Success);
        Assert.False(missing.Success);
        await this.portraits.DidNotReceive().GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
