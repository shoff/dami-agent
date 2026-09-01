using Dami.Core.Scheduling;
using Xunit;

namespace Dami.Core.Tests.Scheduling;

public sealed class CronScheduleTests
{
    [Theory]
    [InlineData("0 7 * * 1-5", "2026-08-31T07:00:00-05:00", true)]
    [InlineData("0 7 * * 1-5", "2026-08-30T07:00:00-05:00", false)]
    [InlineData("*/15 * * * *", "2026-08-31T07:30:00-05:00", true)]
    [InlineData("*/15 * * * *", "2026-08-31T07:31:00-05:00", false)]
    public void IsMatch_Should_Apply_Standard_Five_Field_Cron(
        string expression,
        string timestamp,
        bool expected)
    {
        var schedule = CronSchedule.Parse(expression);

        Assert.Equal(expected, schedule.IsMatch(DateTimeOffset.Parse(timestamp)));
    }

    [Fact]
    public void Next_Should_Respect_The_Jobs_Time_Zone()
    {
        var schedule = CronSchedule.Parse("0 7 * * 1-5");
        var after = new DateTimeOffset(2026, 8, 31, 11, 59, 0, TimeSpan.Zero);

        var next = schedule.Next(after, TimeZoneInfo.FindSystemTimeZoneById("America/Chicago"));

        Assert.Equal(new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero), next);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0 7 * *")]
    [InlineData("60 7 * * *")]
    [InlineData("0 7 * * 8")]
    [InlineData("0 seven * * *")]
    public void Parse_Should_Reject_Invalid_Expressions(string expression)
    {
        Assert.Throws<FormatException>(() => CronSchedule.Parse(expression));
    }
}
