using Dami.Proactive.Security;
using Xunit;

namespace Dami.Proactive.Tests.Security;

public sealed class VersionRangesTests
{
    [Theory]
    [InlineData("4.2.0", "< 4.2.1", true)]
    [InlineData("4.2.1", "< 4.2.1", false)]
    [InlineData("4.2.1", "<= 4.2.1", true)]
    [InlineData("3.0.5", ">= 3.0.0, < 3.1.2", true)]
    [InlineData("3.1.2", ">= 3.0.0, < 3.1.2", false)]
    [InlineData("2.9.0", ">= 3.0.0, < 3.1.2", false)]
    [InlineData("1.0.0", "= 1.0.0", true)]
    [InlineData("1.0.1", "= 1.0.0", false)]
    public void Matches_Should_Evaluate_Advisory_Ranges(string version, string range, bool expected)
    {
        Assert.Equal(expected, VersionRanges.Matches(version, range));
    }

    [Fact]
    public void Matches_Should_Refuse_A_Clause_It_Cannot_Read()
    {
        // Safe-side false: an unreadable range must not fabricate an alert. The cost — a
        // possible false negative — is accepted and recorded here rather than hidden.
        Assert.False(VersionRanges.Matches("1.0.0", "~> 1.0"));
    }
}
