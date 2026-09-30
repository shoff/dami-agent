using Dami.Core.Nutrition;
using Xunit;

namespace Dami.Core.Tests.Nutrition;

public sealed class MealReaderTests
{
    [Fact]
    public void Should_Read_An_Estimate()
    {
        Assert.Equal(("chicken burrito", 650, 40), MealReader.Read("""Sure: {"food":"chicken burrito","calories":650,"protein":40}"""));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not food")]
    [InlineData("""{"food":"burrito","calories":0,"protein":40}""")]
    [InlineData("""{"food":"burrito","calories":9000,"protein":40}""")]
    [InlineData("""{"food":"","calories":600,"protein":40}""")]
    public void An_Implausible_Or_Empty_Reading_Should_Be_Nothing(string reply)
    {
        Assert.Null(MealReader.Read(reply));
    }
}
