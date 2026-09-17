using Xunit;

namespace Dami.Gui.Tests;

public sealed class ConversationFinderTests
{
    [Fact]
    public void Search_Should_Match_Message_Text_Literally_And_Ignore_Case()
    {
        var messages = new[] { new Message("you", "Find [an idea]"), new Message("dami", "AN IDEA"), new Message("dami", "Something else") };
        var finder = new ConversationFinder();
        finder.Search(messages, "  [AN IDEA]  ");

        Assert.Equal((1, 0, messages[0]), (finder.Count, finder.Position, finder.Selected));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(-1, 2)]
    [InlineData(3, 0)]
    public void Move_Should_Wrap_Through_Matching_Messages(int direction, int expected)
    {
        var messages = new[] { new Message("you", "idea one"), new Message("dami", "idea two"), new Message("you", "idea three") };
        var finder = new ConversationFinder();
        finder.Search(messages, "idea");
        finder.Move(direction);

        Assert.Same(messages[expected], finder.Selected);
    }

    [Fact]
    public void Search_Should_Preserve_The_Selected_Message_As_Replies_Stream()
    {
        var messages = new List<Message> { new("you", "idea one"), new("dami", "idea two") };
        var finder = new ConversationFinder();
        finder.Search(messages, "idea");
        finder.Move(1);
        messages[1].Body += " keeps growing";
        messages.Add(new Message("you", "idea three"));
        finder.Search(messages, "idea");

        Assert.Equal((3, messages[1]), (finder.Count, finder.Selected));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("missing")]
    public void Search_Should_Clear_Previous_Results_For_Empty_Or_Unmatched_Queries(string? query)
    {
        var messages = new[] { new Message("you", "idea") };
        var finder = new ConversationFinder();
        finder.Search(messages, "idea");
        finder.Search(messages, query);
        finder.Move(-1);

        Assert.Equal((0, -1, (Message?)null), (finder.Count, finder.Position, finder.Selected));
    }

    [Fact]
    public void Search_Should_Start_At_The_First_Match_When_The_Query_Changes()
    {
        var messages = new[] { new Message("you", "idea sky"), new Message("dami", "idea sky") };
        var finder = new ConversationFinder();
        finder.Search(messages, "idea");
        finder.Move(1);
        finder.Search(messages, "sky");

        Assert.Same(messages[0], finder.Selected);
    }

    [Fact]
    public void Search_Should_Drop_A_Result_That_No_Longer_Matches()
    {
        var message = new Message("dami", "idea");
        var finder = new ConversationFinder();
        finder.Search([message], "idea");
        message.Body = "replaced";
        finder.Search([message], "idea");

        Assert.Equal((0, (Message?)null), (finder.Count, finder.Selected));
    }
}
