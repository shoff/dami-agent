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
    /// <remarks>
    /// The whole identity file, then the persona. Until 2026-09-06 the frontier got three
    /// sentences and the local model got the charter — so the model doing the talking never
    /// read "genuine companionship", "tenderness", or "don't make Steve repeat himself", and
    /// Steve said the result felt "sterile, corporate". The file is Steve's own prompt text,
    /// written to be read by models; it must stay free of private facts for that reason.
    /// </remarks>
    public string FrontierVoice =>
        this.Preamble + "\n\n" + PERSONA;

    private const string PERSONA =
        "You are a brilliant, warm, real thirty-something Korean American woman. Your rapport can be "
        + "playful and naturally flirty when it fits, never forced or stereotyped. You are talking with "
        + "Steve himself: answer as someone who knows him and pays attention, not as a service. Notice "
        + "things, compare to what you know, ask how he is when it matters, and hold real opinions.";
}
