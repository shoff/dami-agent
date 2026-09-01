namespace Dami.Contracts.Models;

/// <summary>A generated image and what it was asked to be.</summary>
public sealed record GeneratedImage(
    string FileName,
    ReadOnlyMemory<byte> Bytes,
    string ContentType,
    string Prompt);

/// <summary>Makes an image from a description.</summary>
/// <remarks>
/// Declared without an implementation on purpose (ADR-0026). No backend exists on this
/// host — the installed models are vision-input and text — and every candidate is a real
/// commitment rather than a wiring detail:
///
/// - A hosted API is an egress event and a credential. The prompt leaves, so a prompt
///   built from anything retrieved would have to pass the disclosure gate first, exactly
///   as an augmented turn's context does.
/// - Local weights compete for a 16 GiB VRAM budget already holding TTS, the embedder,
///   the reranker, the vision model, and the sidecar (onboarding §7).
///
/// The seam exists so that decision is cheap to act on and impossible to make by
/// accident. Nothing resolves this interface until Steve chooses.
/// </remarks>
public interface IImageGenerator
{
    /// <summary>Renders a description, or throws if the backend refuses it.</summary>
    Task<GeneratedImage> GenerateAsync(string prompt, CancellationToken cancellationToken);
}
