using Dami.Contracts.Models;
using Dami.Contracts.Context;
using NSubstitute;
using Xunit;

namespace Dami.Host.Tests;

public sealed class InteractiveImageGeneratorTests
{
    [Fact]
    public async Task SendsAnEgressableUserTurnAndReturnsTheImage()
    {
        var generator = Substitute.For<IImageGenerator>();
        var expected = new GeneratedImage("lake.png", new byte[] { 1, 2 }, "image/png", "lake");
        generator.GenerateAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>())
            .Returns(expected);
        var subject = new InteractiveImageGenerator(generator);

        var result = await subject.GenerateAsync("lake", CancellationToken.None);

        Assert.Same(expected, result);
        await generator.Received(1).GenerateAsync(
            Arg.Is<ImageRequest>(request => request.Prompt == "lake"
                && request.Privacy == PrivacyClass.Egressable), CancellationToken.None);
    }
}
