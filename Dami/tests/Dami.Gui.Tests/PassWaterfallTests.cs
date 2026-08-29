using Xunit;

namespace Dami.Gui.Tests;

public sealed class PassWaterfallTests
{
    private static readonly DateTimeOffset start = new(2026, 8, 27, 22, 47, 44, TimeSpan.Zero);

    private static PassMoment At(double seconds, string type = "EgressRequested", string label = "", string status = "Succeeded") =>
        new(start.AddSeconds(seconds), type, label, status);

    [Fact]
    public void Build_Should_Place_Events_By_When_They_Happened()
    {
        // The whole point. Rows space everything equally whether one event followed the
        // last instantly or four seconds later.
        var pass = PassWaterfall.Build([At(0), At(2), At(4)]);

        Assert.Equal(0, pass[0].BarLeft);
        Assert.Equal(PassWaterfall.TRACK / 2, pass[1].BarLeft);

        // The last event lands on the end of the track and is held back by exactly one
        // minimum bar, so it stays visible rather than being clamped to nothing.
        Assert.Equal(PassWaterfall.TRACK - PassWaterfall.MIN_BAR, pass[2].BarLeft);
    }

    [Fact]
    public void Build_Should_Size_A_Bar_By_The_Wait_That_Followed_It()
    {
        // The scout's rate-limited pass spends most of its time on one call; that call's
        // bar has to be the wide one or the picture says nothing the list did not.
        var pass = PassWaterfall.Build([At(0), At(0.2), At(4), At(4.1)]);

        Assert.True(pass[1].BarWidth > pass[0].BarWidth * 5, "the long wait must dominate");
    }

    [Fact]
    public void Build_Should_Keep_An_Instant_Event_Visible()
    {
        var pass = PassWaterfall.Build([At(0), At(1), At(1), At(2)]);

        Assert.All(pass, item => Assert.True(item.BarWidth >= PassWaterfall.MIN_BAR));
    }

    [Fact]
    public void Build_Should_Not_Run_A_Bar_Past_The_End_Of_The_Track()
    {
        // The last event has nothing after it. Giving it the remaining width would read as
        // a long operation that never happened.
        var pass = PassWaterfall.Build([At(0), At(3)]);

        Assert.All(pass, item =>
            Assert.True(item.BarLeft + item.BarWidth <= PassWaterfall.TRACK + 0.001));
    }

    [Fact]
    public void Build_Should_Survive_A_Pass_That_Took_No_Time()
    {
        var pass = PassWaterfall.Build([At(0), At(0), At(0)]);

        Assert.All(pass, item => Assert.Equal(0, item.BarLeft));
        Assert.All(pass, item => Assert.Equal(PassWaterfall.MIN_BAR, item.BarWidth));
    }

    [Fact]
    public void Build_Should_Label_Offsets_From_The_Start_Of_The_Pass()
    {
        var pass = PassWaterfall.Build([At(0), At(4.25)]);

        Assert.Equal("start", pass[0].Offset);
        Assert.Equal("+4.3s", pass[1].Offset);
    }

    [Theory]
    [InlineData("hnrss.org answered 429", true)]
    [InlineData("hnrss.org answered 500", true)]
    [InlineData("hnrss.org answered 200", false)]
    [InlineData("hnrss.org answered 204", false)]
    [InlineData("nothing to report", false)]
    public void IsAlert_Should_Read_The_Http_Code_Rather_Than_Trust_The_Status(string label, bool expected)
    {
        // That 429 is recorded as a Succeeded event on a Completed run of a healthy
        // service. Every other surface calls it fine.
        Assert.Equal(expected, PassWaterfall.IsAlert(At(0, "EgressCompleted", label)));
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    public void IsAlert_Should_Still_Trust_An_Outright_Failure(string status)
    {
        Assert.True(PassWaterfall.IsAlert(At(0, "ToolStarted", "did a thing", status)));
    }

    [Fact]
    public void Build_Should_Tolerate_An_Empty_Pass()
    {
        Assert.Empty(PassWaterfall.Build([]));
    }
}
