using Dami.Core.Nutrition;
using Xunit;

namespace Dami.Core.Tests.Nutrition;

public sealed class MealReaderTests
{
    [Fact]
    public void Should_Read_An_Estimate()
    {
        var meal = MealReader.Read("""Sure: {"food":"chicken burrito","calories":650,"protein":40,"vitamin_k":"low"}""");

        Assert.Equal(("chicken burrito", 650, 40, "low"), (meal!.Value.Food, meal.Value.Calories, meal.Value.Protein, meal.Value.VitaminK));
    }

    [Theory]
    [InlineData("HIGH", "high")]
    [InlineData("lots", null)]
    [InlineData(null, null)]
    public void The_Vitamin_K_Class_Should_Be_One_Of_Three_Or_Unknown(string? written, string? expected)
    {
        var json = written is null
            ? """{"food":"kale salad","calories":200,"protein":8}"""
            : $$"""{"food":"kale salad","calories":200,"protein":8,"vitamin_k":"{{written}}"}""";

        Assert.Equal(expected, MealReader.Read(json)!.Value.VitaminK);
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
