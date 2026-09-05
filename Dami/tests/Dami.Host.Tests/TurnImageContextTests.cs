using Dami.Contracts.Models;
using NSubstitute;
using Xunit;

namespace Dami.Host.Tests;

public sealed class TurnImageContextTests
{
    [Fact]
    public async Task DescribesAttachmentLocallyAndLabelsDerivedContext()
    {
        var vision = Substitute.For<IVisionClient>();
        vision.DescribeAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("a red bicycle");
        var subject = new TurnImageContext(vision);

        var result = await subject.DescribeAsync(
            new TurnImageAttachment("bike.png", "image/png", [1, 2, 3]), CancellationToken.None);

        Assert.Equal(["Local vision description of bike.png: a red bicycle"], result);
        await vision.Received(1).DescribeAsync(
            Arg.Is<ReadOnlyMemory<byte>>(bytes => bytes.ToArray().SequenceEqual(new byte[] { 1, 2, 3 })),
            Arg.Any<string>(), CancellationToken.None);
    }
}
