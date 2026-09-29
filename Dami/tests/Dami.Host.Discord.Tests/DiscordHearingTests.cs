using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Gateway.Discord;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Dami.Host.Discord.Tests;

/// <summary>A voice note is Steve's words: transcribed on this host, then treated as text.</summary>
public sealed class DiscordHearingTests
{
    private readonly ITranscriptionClient transcription = Substitute.For<ITranscriptionClient>();
    private readonly IDiscordRest rest = Substitute.For<IDiscordRest>();

    private static InboundAttachment Voice(int size = 40_000) =>
        new("voice-message.ogg", "https://cdn.discordapp.com/v.ogg", "audio/ogg", size);

    private static InboundMessage From(string text, params InboundAttachment[] attachments) =>
        new("owner", "chan-1", text, DateTimeOffset.UnixEpoch) { Attachments = attachments };

    private DiscordHearing Subject() => new(
        this.transcription, this.rest,
        new DiscordOptions { Token = "t", OwnerUserId = "1", Enabled = true },
        NullLogger<DiscordHearing>.Instance);

    public DiscordHearingTests()
    {
        this.rest.DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new byte[] { 1, 2, 3 });
    }

    [Fact]
    public void An_Audio_Attachment_Should_Be_Recognised()
    {
        Assert.True(Voice().IsAudio);
        Assert.False(new InboundAttachment("a.png", "u", "image/png", 1).IsAudio);
    }

    [Fact]
    public async Task A_Voice_Note_Should_Become_The_Message_Text()
    {
        this.transcription.TranscribeAsync(Arg.Any<byte[]>(), "voice-message.ogg", Arg.Any<CancellationToken>())
            .Returns(" remind me to call the vet tomorrow ");

        var heard = await this.Subject().HearAsync(From("", Voice()), CancellationToken.None);

        Assert.Equal("remind me to call the vet tomorrow", heard.Text);
    }

    [Fact]
    public async Task Typed_Words_Should_Come_First_And_The_Voice_Note_After()
    {
        this.transcription.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("and the dentist on Friday");

        var heard = await this.Subject().HearAsync(From("two things", Voice()), CancellationToken.None);

        Assert.Equal("two things\nand the dentist on Friday", heard.Text);
    }

    [Fact]
    public async Task A_Message_Without_Audio_Should_Pass_Untouched()
    {
        var message = From("hello");

        var heard = await this.Subject().HearAsync(message, CancellationToken.None);

        Assert.Same(message, heard);
        await this.transcription.DidNotReceiveWithAnyArgs().TranscribeAsync(default!, default!, default);
    }

    [Fact]
    public async Task A_Voice_Note_That_Cannot_Be_Transcribed_Should_Still_Be_Answered()
    {
        // Silence would leave Steve wondering whether it arrived at all.
        this.transcription.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new HttpRequestException("connection refused"));

        var heard = await this.Subject().HearAsync(From("", Voice()), CancellationToken.None);

        Assert.Contains("could not be transcribed", heard.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_Oversized_Clip_Should_Not_Be_Downloaded()
    {
        var heard = await this.Subject().HearAsync(From("", Voice(size: 60 * 1024 * 1024)), CancellationToken.None);

        Assert.Contains("too long", heard.Text, StringComparison.Ordinal);
        await this.rest.DidNotReceiveWithAnyArgs().DownloadAsync(default!, default);
    }
}
