using Dami.Proactive.Portrait;
using Xunit;

namespace Dami.Proactive.Tests.Portrait;

public sealed class PortraitSlotTests
{
    private static DateTimeOffset AtLocalHour(int hour) =>
        new(2026, 8, 31, hour, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(7, "morning")]
    [InlineData(11, "morning")]
    [InlineData(12, "midday")]
    [InlineData(16, "midday")]
    [InlineData(17, "evening")]
    [InlineData(23, "evening")]
    [InlineData(3, "evening")]
    public void Of_Should_Name_The_Slot_From_The_Local_Hour(int hour, string expected)
    {
        // The scheduler is interval-based and knows nothing about clock time, so the slot
        // is read from the clock at run time rather than assumed from the cadence.
        Assert.Equal(expected, PortraitSlot.Of(AtLocalHour(hour), utcOffsetHours: 0));
    }

    [Fact]
    public void Of_Should_Apply_The_Local_Offset()
    {
        // 02:00 UTC is 21:00 the previous evening at -5.
        Assert.Equal("evening", PortraitSlot.Of(AtLocalHour(2), utcOffsetHours: -5));
    }

    [Fact]
    public void FileNameFor_Should_Date_And_Slot_The_File()
    {
        Assert.Equal(
            "dami-2026-08-31-evening.png",
            PortraitSlot.FileNameFor(AtLocalHour(20), utcOffsetHours: 0));
    }

    [Fact]
    public void FileNameFor_Should_Use_The_Local_Date_Not_The_Utc_One()
    {
        // 01:00 UTC on the 31st is still the 30th locally at -5; naming it the 31st would
        // put two evenings in one file.
        Assert.StartsWith(
            "dami-2026-08-30-", PortraitSlot.FileNameFor(AtLocalHour(1), utcOffsetHours: -5),
            StringComparison.Ordinal);
    }
}
