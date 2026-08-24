using System.Text.Json;

namespace Dami.Capabilities.Native.Tests;

public sealed class ReadFileCapabilityHandlerTests : IDisposable
{
    private readonly string outside;
    private readonly string scratch;

    public ReadFileCapabilityHandlerTests()
    {
        this.scratch = Path.Combine(
            Path.GetTempPath(),
            "dami-read-capability-" + Guid.NewGuid().ToString("N"));
        this.outside = Path.Combine(
            Path.GetTempPath(),
            "dami-read-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.scratch);
        Directory.CreateDirectory(this.outside);
    }

    public void Dispose()
    {
        Directory.Delete(this.scratch, recursive: true);
        Directory.Delete(this.outside, recursive: true);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Read_A_Rooted_File_With_Verifiable_Evidence()
    {
        await File.WriteAllTextAsync(Path.Combine(this.scratch, "notes.txt"), "bounded content");
        var handler = new ReadFileCapabilityHandler(
            new ReadFileCapabilityOptions { RootDirectory = this.scratch, MaxBytes = 1024 });
        var arguments = JsonSerializer.SerializeToElement(new { path = "notes.txt" });

        var result = await handler.ExecuteAsync(arguments, CancellationToken.None);

        Assert.Equal("bounded content", result.Output);
        Assert.Equal("notes.txt", result.Evidence["path"]);
        Assert.Equal("15", result.Evidence["bytes"]);
        Assert.Equal(64, result.Evidence["sha256"].Length);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Reject_A_Directory_Symlink_That_Escapes_The_Root()
    {
        await File.WriteAllTextAsync(Path.Combine(this.outside, "secret.txt"), "outside");
        Directory.CreateSymbolicLink(Path.Combine(this.scratch, "link"), this.outside);
        var handler = new ReadFileCapabilityHandler(
            new ReadFileCapabilityOptions { RootDirectory = this.scratch, MaxBytes = 1024 });
        var arguments = JsonSerializer.SerializeToElement(new { path = "link/secret.txt" });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => handler.ExecuteAsync(arguments, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_Should_Reject_A_File_Larger_Than_The_Byte_Bound()
    {
        await File.WriteAllTextAsync(Path.Combine(this.scratch, "large.txt"), "12345");
        var handler = new ReadFileCapabilityHandler(
            new ReadFileCapabilityOptions { RootDirectory = this.scratch, MaxBytes = 4 });
        var arguments = JsonSerializer.SerializeToElement(new { path = "large.txt" });

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => handler.ExecuteAsync(arguments, CancellationToken.None));

        Assert.Contains("4 bytes", exception.Message, StringComparison.Ordinal);
    }
}
