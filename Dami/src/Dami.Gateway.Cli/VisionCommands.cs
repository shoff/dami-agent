using Dami.Contracts.Models;

namespace Dami.Gateway.Cli;

/// <summary>Local vision from the shell.</summary>
public sealed class VisionCommands
{
    private readonly IVisionClient visionClient;

    /// <summary>Creates the commands.</summary>
    public VisionCommands(IVisionClient visionClient)
    {
        ArgumentNullException.ThrowIfNull(visionClient);
        this.visionClient = visionClient;
    }

    /// <summary>Captions one image file. The image never leaves the host.</summary>
    public async Task<int> CaptionAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
        {
            await Console.Error.WriteLineAsync($"no such file: {path}").ConfigureAwait(false);
            return 1;
        }

        var image = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        Console.WriteLine("looking (local vision model)...");

        var description = await this.visionClient.DescribeAsync(
            image,
            "Caption this image in one sentence, then list 3 short category tags.",
            cancellationToken).ConfigureAwait(false);

        Console.WriteLine(description);
        return 0;
    }
}
