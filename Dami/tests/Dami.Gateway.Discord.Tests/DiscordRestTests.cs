using System.Net;
using System.Text;
using Dami.Contracts.Privacy;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dami.Gateway.Discord.Tests;

public sealed class DiscordRestTests
{
    /// <summary>Captures the request instead of sending it.</summary>
    private sealed class Recording : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string Body { get; private set; } = string.Empty;

        public byte[] Payload { get; set; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Request = request;
            if (request.Content is not null)
            {
                this.Body = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(this.Payload),
            };
        }
    }

    private static (DiscordRest Client, Recording Handler) Create()
    {
        var handler = new Recording();
        return (
            new DiscordRest(new HttpClient(handler), "a-token", NullLogger<DiscordRest>.Instance),
            handler);
    }

    [Fact]
    public async Task PostMessageWithFilesAsync_Should_Send_Multipart_When_There_Is_A_File()
    {
        var (rest, handler) = Create();

        await rest.PostMessageWithFilesAsync(
            "chan-1",
            "here it is",
            [new OutboundAttachment("chart.png", new ReadOnlyMemory<byte>([1, 2, 3]), "image/png")],
            CancellationToken.None);

        Assert.Contains(
            "multipart/form-data",
            handler.Request!.Content!.Headers.ContentType!.MediaType,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostMessageWithFilesAsync_Should_Name_The_File_Discord_Expects()
    {
        var (rest, handler) = Create();

        await rest.PostMessageWithFilesAsync(
            "chan-1",
            "here it is",
            [new OutboundAttachment("chart.png", new ReadOnlyMemory<byte>([1, 2, 3]), "image/png")],
            CancellationToken.None);

        Assert.Contains("files[0]", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostMessageWithFilesAsync_Should_Fall_Back_To_A_Plain_Post_With_No_Files()
    {
        // A plain reply must not become a multipart upload just because the path exists.
        var (rest, handler) = Create();

        await rest.PostMessageWithFilesAsync("chan-1", "just words", [], CancellationToken.None);

        Assert.Equal(
            "application/json",
            handler.Request!.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task DownloadAsync_Should_Return_The_Bytes()
    {
        var (rest, handler) = Create();
        handler.Payload = Encoding.UTF8.GetBytes("not really a png");

        var bytes = await rest.DownloadAsync(
            "https://cdn.discordapp.com/bolt.png", CancellationToken.None);

        Assert.Equal("not really a png", Encoding.UTF8.GetString(bytes.ToArray()));
    }

    [Fact]
    public async Task DownloadAsync_Should_Not_Send_The_Bot_Token_To_A_Cdn()
    {
        // The URL carries its own signature; sending credentials to a host that did not
        // ask for them is how they leak.
        var (rest, handler) = Create();

        await rest.DownloadAsync("https://cdn.discordapp.com/bolt.png", CancellationToken.None);

        Assert.Null(handler.Request!.Headers.Authorization);
    }
    [Fact]
    public async Task PostMessageAsync_Should_Send_The_Bot_Auth_Scheme()
    {
        // Mutation testing found this untested: dropping the "Bot" scheme 401s every API
        // call and the gateway goes mute, with nothing in the suite noticing.
        var (rest, handler) = Create();

        await rest.PostMessageAsync("chan-1", "hello", CancellationToken.None);

        Assert.Equal("Bot", handler.Request!.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task PostMessageWithFilesAsync_Should_Send_The_Bot_Auth_Scheme()
    {
        var (rest, handler) = Create();

        await rest.PostMessageWithFilesAsync(
            "chan-1", "hi",
            [new OutboundAttachment("a.png", new ReadOnlyMemory<byte>([1]), "image/png")],
            CancellationToken.None);

        Assert.Equal("Bot", handler.Request!.Headers.Authorization!.Scheme);
    }
    [Theory]
    [InlineData("http://127.0.0.1:11434/api/tags")]
    [InlineData("https://evil.example.com/x.png")]
    [InlineData("http://cdn.discordapp.com/x.png")]
    [InlineData("file:///etc/passwd")]
    public async Task DownloadAsync_Should_Refuse_Anything_That_Is_Not_A_Discord_Cdn_Url(string url)
    {
        // The URL arrives inside a gateway frame and is remote input. Fetching whatever it
        // names would make this a request forwarder inside the host, terminating in a
        // local model whose caption is then egressed.
        var (rest, handler) = Create();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await rest.DownloadAsync(url, CancellationToken.None));

        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task DownloadAsync_Should_Accept_A_Discord_Cdn_Url()
    {
        var (rest, handler) = Create();
        handler.Payload = Encoding.UTF8.GetBytes("png");

        var bytes = await rest.DownloadAsync(
            "https://cdn.discordapp.com/attachments/1/2/x.png", CancellationToken.None);

        Assert.Equal("png", Encoding.UTF8.GetString(bytes.ToArray()));
    }
}
