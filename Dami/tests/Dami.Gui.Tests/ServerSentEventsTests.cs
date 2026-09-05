using Xunit;

namespace Dami.Gui.Tests;

public sealed class ServerSentEventsTests
{
    private static async Task<List<StreamedFragment>> ReadAsync(string body)
    {
        using var reader = new StringReader(body);
        var fragments = new List<StreamedFragment>();
        await foreach (var fragment in ServerSentEvents.ReadAsync(reader, CancellationToken.None))
        {
            fragments.Add(fragment);
        }

        return fragments;
    }

    [Fact]
    public async Task Plain_Data_Blocks_Are_Text_And_Multi_Line_Blocks_Are_Rejoined()
    {
        var fragments = await ReadAsync("data: hello\n\ndata: line one\ndata: line two\n\n");

        Assert.Equal(["hello", "line one\nline two"], fragments.Select(fragment => fragment.Text));
        Assert.All(fragments, fragment => Assert.Null(fragment.PictureFileName));
    }

    [Fact]
    public async Task A_Picture_Event_Names_A_Gallery_File()
    {
        var fragments = await ReadAsync("data: here you go\n\nevent: picture\ndata: dami-1.png\n\n");

        Assert.Equal("dami-1.png", fragments[1].PictureFileName);
        Assert.Null(fragments[1].Text);
    }

    [Fact]
    public async Task An_Unknown_Event_Is_Ignored_Rather_Than_Shown()
    {
        var fragments = await ReadAsync("event: heartbeat\ndata: 1\n\ndata: text\n\n");

        Assert.Equal("text", Assert.Single(fragments).Text);
    }
}
