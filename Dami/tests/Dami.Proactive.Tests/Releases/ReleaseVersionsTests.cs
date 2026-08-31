using Dami.Proactive.Releases;
using Xunit;

namespace Dami.Proactive.Tests.Releases;

public sealed class ReleaseVersionsTests
{
    [Theory]
    [InlineData("595.90  https://download.nvidia.com/...", "595.90")]
    [InlineData("PostgreSQL 17.6", "17.6")]
    [InlineData("v0.12.3", "0.12.3")]
    [InlineData("10.0.401 (SDK)", "10.0.401")]
    [InlineData("no version here", null)]
    public void Extract_Should_Find_The_First_Dotted_Version(string text, string? expected)
    {
        Assert.Equal(expected, ReleaseVersions.Extract(text));
    }

    [Theory]
    [InlineData("595.90", "595.84", true)]
    [InlineData("595.84", "595.84", false)]
    [InlineData("595.80", "595.84", false)]
    [InlineData("10.0.401", "10.0.400", true)]
    [InlineData("17.0", "16.15", true)]
    [InlineData("10.0.400.1", "10.0.400", true)]
    [InlineData("10.0.400", "10.0.400.1", false)]
    public void IsNewer_Should_Compare_Segments_Numerically(
        string candidate, string baseline, bool expected)
    {
        // Numeric, not lexical: "595.9" would beat "595.84" as text and lose as a version.
        Assert.Equal(expected, ReleaseVersions.IsNewer(candidate, baseline));
    }

    [Fact]
    public void IsNewer_Should_Not_Let_A_Two_Digit_Segment_Lose_To_A_One_Digit_One()
    {
        Assert.True(ReleaseVersions.IsNewer("595.100", "595.99"));
    }
}
