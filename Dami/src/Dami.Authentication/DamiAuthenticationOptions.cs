namespace Dami.Authentication;

/// <summary>Configures the local OpenID Connect authority (ADR-0020).</summary>
public sealed class DamiAuthenticationOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SECTION_NAME = "Authentication";

    /// <summary>Gets or sets whether the authority and API authentication are enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the stable issuer URI.</summary>
    public string Issuer { get; set; } = "http://127.0.0.1:5810/";

    /// <summary>Gets or sets whether loopback HTTP is permitted before the TLS cutover.</summary>
    public bool AllowInsecureLoopback { get; set; }

    /// <summary>Gets or sets whether process-local keys may be used in isolated tests.</summary>
    public bool UseEphemeralKeys { get; set; }

    /// <summary>Gets or sets the external PKCS#12 signing-certificate path.</summary>
    public string? SigningCertificatePath { get; set; }

    /// <summary>Gets or sets the signing-certificate password supplied by secret configuration.</summary>
    public string? SigningCertificatePassword { get; set; }

    /// <summary>Gets or sets the external PKCS#12 encryption-certificate path.</summary>
    public string? EncryptionCertificatePath { get; set; }

    /// <summary>Gets or sets the encryption-certificate password supplied by secret configuration.</summary>
    public string? EncryptionCertificatePassword { get; set; }
}
