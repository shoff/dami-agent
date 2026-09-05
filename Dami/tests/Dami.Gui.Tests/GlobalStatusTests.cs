using Xunit;

namespace Dami.Gui.Tests;

public sealed class GlobalStatusTests
{
    [Fact]
    public void Working_Status_Is_Immediately_Visible_As_Busy()
    {
        var status = GlobalStatus.Working("Creating a Dami portrait…");

        Assert.True(status.IsBusy);
        Assert.Equal("WORKING", status.Label);
        Assert.Equal("Creating a Dami portrait…", status.Message);
    }

    [Fact]
    public void Failure_Status_Preserves_The_Actual_Error()
    {
        var status = GlobalStatus.Failure("subscription provider is disabled");

        Assert.False(status.IsBusy);
        Assert.Equal("ERROR", status.Label);
        Assert.Contains("subscription provider is disabled", status.Message);
    }
}
