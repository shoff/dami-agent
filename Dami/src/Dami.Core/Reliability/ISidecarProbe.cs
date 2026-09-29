namespace Dami.Core.Reliability;

/// <summary>A real request to one local sidecar, not its health endpoint.</summary>
/// <remarks>
/// On 2026-09-29 the speech sidecar had lost the GPU since 09-08: /health answered 200 and
/// every transcription failed with "no CUDA-capable device". Only doing the work proves it.
/// </remarks>
public interface ISidecarProbe
{
    /// <summary>What the sidecar is, as Steve would call it.</summary>
    string Name { get; }

    /// <summary>Does one small piece of real work; throws when the sidecar cannot.</summary>
    Task ProbeAsync(CancellationToken cancellationToken);
}
