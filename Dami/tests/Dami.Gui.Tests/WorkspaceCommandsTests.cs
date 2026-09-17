using Avalonia.Input;
using Xunit;

namespace Dami.Gui.Tests;

public sealed class WorkspaceCommandsTests
{
    [Fact]
    public void Search_Should_Match_Words_Across_Title_And_Description()
    {
        var matches = WorkspaceCommands.Search("  EVENTS   activity ");

        Assert.Equal(new[] { "activity" }, matches.Select(item => item.Id));
    }
    [Theory]
    [InlineData(Key.D1, KeyModifiers.Control, "chat")]
    [InlineData(Key.D7, KeyModifiers.Control, "network")]
    [InlineData(Key.L, KeyModifiers.Control, "compose")]
    [InlineData(Key.D1, KeyModifiers.None, null)]
    [InlineData(Key.D1, KeyModifiers.Control | KeyModifiers.Shift, null)]
    [InlineData(Key.D8, KeyModifiers.Control, "research")]
    public void FromKey_Should_Resolve_Only_Exact_Workspace_Shortcuts(
        Key key, KeyModifiers modifiers, string? expected)
    {
        Assert.Equal(expected, WorkspaceCommands.FromKey(key, modifiers)?.Id);
    }
    [Theory]
    [InlineData(null, 10)]
    [InlineData("   ", 10)]
    [InlineData("no-such-command", 0)]
    public void Search_Should_Handle_Empty_And_Unmatched_Queries(string? query, int expected)
    {
        Assert.Equal(expected, WorkspaceCommands.Search(query).Count);
    }

    [Theory]
    [InlineData("Conversation", 0)]
    [InlineData("Plan & tasks", 1)]
    [InlineData("Activity", 2)]
    [InlineData("Workers", 3)]
    [InlineData("Health", 4)]
    [InlineData("Gallery", 5)]
    [InlineData("Network", 6)]
    public void Search_Should_Open_The_Corresponding_Page(string title, int expected)
    {
        var command = WorkspaceCommands.Search(title).Single(item => item.Title == title);

        Assert.Equal(expected, command.PageIndex);
    }
}
