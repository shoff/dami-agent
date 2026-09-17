using Xunit;

namespace Dami.Gui.Tests;

public sealed class WorkspaceStateTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void Activate_Should_Toggle_Focus_And_Open_Conversation(int count, bool expected)
    {
        var state = new WorkspaceState();
        var command = WorkspaceCommands.Search("toggle focus").Single();
        for (var index = 0; index < count; index++)
        {
            state.Activate(command);
        }

        Assert.Equal((0, expected), (state.PageIndex, state.IsFocusMode));
    }
    [Fact]
    public void Activate_Should_Keep_Focus_Preference_When_Changing_Pages()
    {
        var state = new WorkspaceState();
        state.Activate(WorkspaceCommands.Search("toggle focus")[0]);
        state.Activate(WorkspaceCommands.Search("network")[0]);

        Assert.Equal((6, true), (state.PageIndex, state.IsFocusMode));
    }

    [Fact]
    public void Activate_Should_Reject_Null_Command()
    {
        var state = new WorkspaceState();

        Assert.Throws<ArgumentNullException>(() => state.Activate(null!));
    }
}
