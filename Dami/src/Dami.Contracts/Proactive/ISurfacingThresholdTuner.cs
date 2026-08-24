namespace Dami.Contracts.Proactive;

/// <summary>Tunes a service's surfacing threshold from Steve's recorded reactions.</summary>
/// <remarks>
/// The register's open question is "how does it self-tune without gaming itself"; the
/// answer here is that there is nothing to game: the effective threshold is a pure,
/// bounded function of explicit reactions. Silence moves nothing — an unread surfacing
/// is not evidence — so staying quiet cannot improve the tuner's standing, and the
/// clamp means no reaction history can tune the service into "surface everything" or
/// "never speak again".
/// </remarks>
public interface ISurfacingThresholdTuner
{
    /// <summary>The threshold the service should use this pass.</summary>
    Task<double> EffectiveThresholdAsync(
        string serviceName,
        double baseThreshold,
        CancellationToken cancellationToken);
}
