using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Dami.Providers.Tests;

public sealed class FallbackImageGeneratorTests
{
    private static readonly GeneratedImage primaryImage = new("primary.png", new byte[] { 1 }, "image/png", "p");
    private static readonly GeneratedImage backupImage = new("backup.png", new byte[] { 2 }, "image/png", "p");

    private readonly IImageGenerator primary = Substitute.For<IImageGenerator>();
    private readonly IImageGenerator backup = Substitute.For<IImageGenerator>();

    public FallbackImageGeneratorTests()
    {
        this.primary.GenerateAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>()).Returns(primaryImage);
        this.backup.GenerateAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>()).Returns(backupImage);
    }

    private FallbackImageGenerator Create() =>
        new(this.primary, this.backup, NullLogger<FallbackImageGenerator>.Instance);

    private static ImageRequest Request(PrivacyClass privacy = PrivacyClass.Egressable) =>
        new("a portrait", "daily portrait (evening)", privacy, Guid.NewGuid(), ExecutionOrigin.ScheduledService);

    private void PrimaryThrows(Exception exception) =>
        this.primary.GenerateAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>())
            .Returns<GeneratedImage>(_ => throw exception);

    [Fact]
    public void Should_Reject_A_Null_Primary()
    {
        Assert.Throws<ArgumentNullException>(
            () => new FallbackImageGenerator(null!, this.backup, NullLogger<FallbackImageGenerator>.Instance));
    }

    [Fact]
    public async Task Should_Return_The_Primary_Image_When_It_Draws()
    {
        var image = await this.Create().GenerateAsync(Request(), CancellationToken.None);

        Assert.Same(primaryImage, image);
    }

    [Fact]
    public async Task Should_Not_Touch_The_Backup_When_The_Primary_Draws()
    {
        // The backup bills per image; it is a backup, not a second opinion.
        await this.Create().GenerateAsync(Request(), CancellationToken.None);

        await this.backup.DidNotReceive().GenerateAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Use_The_Backup_When_The_Primary_Refuses()
    {
        this.PrimaryThrows(new EgressRefusedException("the subscription image provider is not enabled"));

        var image = await this.Create().GenerateAsync(Request(), CancellationToken.None);

        Assert.Same(backupImage, image);
    }

    [Fact]
    public async Task Should_Use_The_Backup_When_The_Primary_Fails()
    {
        // "Refused" in Steve's words covers the subscription tool returning nothing or
        // timing out: what he sees either way is no picture.
        this.PrimaryThrows(new InvalidOperationException("The subscription image tool returned no image file."));

        var image = await this.Create().GenerateAsync(Request(), CancellationToken.None);

        Assert.Same(backupImage, image);
    }

    [Fact]
    public async Task Should_Use_The_Backup_When_The_Primary_Times_Out()
    {
        this.PrimaryThrows(new TimeoutException("codex exceeded 480s"));

        var image = await this.Create().GenerateAsync(Request(), CancellationToken.None);

        Assert.Same(backupImage, image);
    }

    [Fact]
    public async Task Should_Hand_The_Backup_The_Same_Request()
    {
        this.PrimaryThrows(new EgressRefusedException("not enabled"));
        var request = Request();

        await this.Create().GenerateAsync(request, CancellationToken.None);

        await this.backup.Received().GenerateAsync(request, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Never_Fall_Back_For_A_Prompt_That_Is_Not_Egressable()
    {
        // D-012: a privacy refusal is not a provider outage. Local-only content does not
        // get a second door to leave through.
        this.PrimaryThrows(new EgressRefusedException("the image prompt is not Egressable"));

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => this.Create().GenerateAsync(Request(PrivacyClass.LocalOnly), CancellationToken.None));

        await this.backup.DidNotReceive().GenerateAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Not_Fall_Back_When_The_Caller_Cancelled()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        this.PrimaryThrows(new OperationCanceledException(cancelled.Token));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => this.Create().GenerateAsync(Request(), cancelled.Token));

        await this.backup.DidNotReceive().GenerateAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Throw_The_Backups_Failure_When_Both_Fail()
    {
        this.PrimaryThrows(new EgressRefusedException("not enabled"));
        this.backup.GenerateAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>())
            .Returns<GeneratedImage>(_ => throw new EgressRefusedException("no API key is configured"));

        var exception = await Assert.ThrowsAsync<EgressRefusedException>(
            () => this.Create().GenerateAsync(Request(), CancellationToken.None));

        Assert.Contains("no API key is configured", exception.Message, StringComparison.Ordinal);
    }
}
