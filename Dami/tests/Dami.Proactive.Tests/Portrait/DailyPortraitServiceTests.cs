using Dami.Contracts.Context;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Proactive.Portrait;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.Portrait;

public sealed class DailyPortraitServiceTests : IDisposable
{
    private static readonly DateTimeOffset now = new(2026, 8, 31, 23, 0, 0, TimeSpan.Zero);

    private readonly string directory = Path.Combine(
        Path.GetTempPath(), "dami-portrait-tests-" + Guid.NewGuid().ToString("N"));

    private readonly IImageGenerator generator = Substitute.For<IImageGenerator>();

    public DailyPortraitServiceTests()
    {
        this.generator
            .GenerateAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GeneratedImage("x.png", new ReadOnlyMemory<byte>([1, 2, 3]), "image/png", "p"));
    }

    public void Dispose()
    {
        if (Directory.Exists(this.directory))
        {
            Directory.Delete(this.directory, recursive: true);
        }
    }

    [Fact]
    public async Task Should_Do_Nothing_When_Disabled()
    {
        // It spends money per pass; inheriting it by deploying would be the wrong default.
        var result = await this.Service(enabled: false)
            .RunPassAsync(Context(), CancellationToken.None);

        await this.generator.DidNotReceive()
            .GenerateAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>());
        Assert.Empty(result.Surfacings);
    }

    [Fact]
    public async Task Should_Write_The_Image_To_Disk()
    {
        await this.Service().RunPassAsync(Context(), CancellationToken.None);

        Assert.Single(Directory.GetFiles(this.directory, "*.png"));
    }

    [Fact]
    public async Task Should_Name_The_File_For_Its_Slot()
    {
        // 23:00 UTC is 18:00 at -5 — the evening pass.
        await this.Service().RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal(
            "dami-2026-08-31-evening.png",
            Path.GetFileName(Directory.GetFiles(this.directory, "*.png")[0]));
    }

    [Fact]
    public async Task Should_Surface_It_So_Steve_Learns_It_Exists()
    {
        var result = await this.Service().RunPassAsync(Context(), CancellationToken.None);

        Assert.Contains("evening", Assert.Single(result.Surfacings).Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_Put_The_Slot_Into_The_Prompt()
    {
        await this.Service().RunPassAsync(Context(), CancellationToken.None);

        await this.generator.Received(1).GenerateAsync(
            Arg.Is<ImageRequest>(request =>
                request.Prompt.Contains("evening", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Mark_The_Request_Egressable_And_Never_Local()
    {
        // A prompt this service composes carries nothing retrieved, which is exactly why
        // it may leave. The generator refuses anything else.
        await this.Service().RunPassAsync(Context(), CancellationToken.None);

        await this.generator.Received(1).GenerateAsync(
            Arg.Is<ImageRequest>(request => request.Privacy == PrivacyClass.Egressable),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Not_Regenerate_A_Slot_Already_On_Disk()
    {
        // Three passes a day against a paid API; a restart must not buy the same picture
        // twice.
        var service = this.Service();
        await service.RunPassAsync(Context(), CancellationToken.None);
        await service.RunPassAsync(Context(), CancellationToken.None);

        await this.generator.Received(1).GenerateAsync(
            Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Complete_Quietly_When_Egress_Is_Refused()
    {
        this.generator
            .GenerateAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>())
            .Returns<GeneratedImage>(_ => throw new EgressRefusedException("host not allowlisted"));

        var result = await this.Service().RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal(ProactiveStatus.Completed, result.Status);
        Assert.Empty(result.Surfacings);
    }

    private static ProactiveContext Context() => new(Guid.NewGuid(), now, null);

    private DailyPortraitService Service(bool enabled = true)
    {
        return new DailyPortraitService(
            this.generator,
            Options.Create(new DailyPortraitOptions
            {
                Enabled = enabled,
                OutputDirectory = this.directory,
                LocalUtcOffsetHours = -5,
            }),
            new FakeTimeProvider(now),
            NullLogger<DailyPortraitService>.Instance);
    }
}
