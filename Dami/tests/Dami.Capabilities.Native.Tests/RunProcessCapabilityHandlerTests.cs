using System.Text.Json;

namespace Dami.Capabilities.Native.Tests;

public sealed class RunProcessCapabilityHandlerTests : IDisposable
{
    private readonly string scratch;

    public RunProcessCapabilityHandlerTests()
    {
        this.scratch = Path.Combine(
            Path.GetTempPath(),
            "dami-process-capability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.scratch);
    }

    public void Dispose()
    {
        Directory.Delete(this.scratch, recursive: true);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Pass_Literal_Arguments_Without_A_Shell()
    {
        var marker = Path.Combine(this.scratch, "shell-injection-marker");
        var literal = $"safe; touch {marker}";
        var handler = new RunProcessCapabilityHandler(new RunProcessCapabilityOptions
        {
            RootDirectory = this.scratch,
            AllowedExecutables = new Dictionary<string, string> { ["printf"] = "/usr/bin/printf" },
            MaxOutputBytes = 1024,
        });
        var arguments = JsonSerializer.SerializeToElement(new
        {
            executable = "printf",
            arguments = new[] { "%s", literal },
        });

        var result = await handler.ExecuteAsync(arguments, CancellationToken.None);

        Assert.Equal(literal, result.Output);
        Assert.False(File.Exists(marker));
        Assert.Equal("0", result.Evidence["exit_code"]);
        Assert.Equal("printf", result.Evidence["executable"]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Reject_Output_Above_The_Combined_Byte_Bound()
    {
        var handler = new RunProcessCapabilityHandler(new RunProcessCapabilityOptions
        {
            RootDirectory = this.scratch,
            AllowedExecutables = new Dictionary<string, string> { ["printf"] = "/usr/bin/printf" },
            MaxOutputBytes = 4,
        });
        var arguments = JsonSerializer.SerializeToElement(new
        {
            executable = "printf",
            arguments = new[] { "%s", "12345" },
        });

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => handler.ExecuteAsync(arguments, CancellationToken.None));

        Assert.Contains("4 bytes", exception.Message, StringComparison.Ordinal);
    }
}
