namespace Dami.Privacy;

/// <summary>The egress boundary's policy, bound from configuration.</summary>
public sealed class EgressOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SECTION_NAME = "Egress";

    /// <summary>
    /// Hosts a request may go to. Empty means nothing leaves — deny by default.
    /// </summary>
    /// <remarks>
    /// An allowlist rather than a blocklist, because the failure mode of a blocklist is
    /// silence and the failure mode of an allowlist is a loud refusal.
    /// </remarks>
    public IList<string> AllowedHosts { get; } = [];

    /// <summary>
    /// Fragments that must not appear anywhere in an outgoing URI.
    /// </summary>
    /// <remarks>
    /// The honest limitation, stated: detecting "profile-derived content" in general is
    /// not decidable by string matching. The real enforcement is structural — the narrow
    /// request type, the allowlist, and local-only services having no egress client at
    /// all. This list catches the crude leaks (a name, a street, an account number
    /// pasted into a query), and it is a tripwire, not the wall.
    /// </remarks>
    public IList<string> ForbiddenFragments { get; } = [];

    /// <summary>Largest request body permitted through a body-capable egress door.</summary>
    public int MaxRequestBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>Largest response body accepted into memory.</summary>
    public int MaxResponseBytes { get; set; } = 2 * 1024 * 1024;
}
