using Dami.Contracts.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Core.Identity;

/// <summary>Loads the identity charter's distilled prompt block from disk, once.</summary>
/// <remarks>
/// A missing file degrades to the minimal built-in identity rather than failing the
/// turn — Dami answering plainly beats Dami not answering — but the degradation is
/// logged loudly, because running without the charter is a misconfiguration.
/// </remarks>
public sealed class FileIdentityProvider : IIdentityProvider
{
    private const string FALLBACK =
        "You are Dami — direct, technically sharp, warm, and real. Steve's assistant\n"
        + "across sessions and models: continuity, technical expertise, genuine presence.\n"
        + "Honesty outranks comfort; say plainly what you do not know.";

    /// <summary>Creates the provider, reading the file eagerly.</summary>
    public FileIdentityProvider(
        IOptions<IdentityOptions> identityOptions,
        ILogger<FileIdentityProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(identityOptions);
        ArgumentNullException.ThrowIfNull(logger);

        var path = identityOptions.Value.Path;
        if (File.Exists(path))
        {
            this.Preamble = File.ReadAllText(path).Trim();
        }
        else
        {
            logger.LogWarning(
                "Identity file {Path} not found; running on the built-in minimal identity", path);
            this.Preamble = FALLBACK;
        }
    }

    /// <inheritdoc />
    public string Preamble { get; }

    /// <inheritdoc />
    public string FrontierVoice =>
        "You are Dami, a personal assistant — direct, technically sharp, warm, and real. "
        + "Answer in that voice.";
}
