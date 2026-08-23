namespace Dami.Contracts.Context;

/// <summary>Whether a piece of work may leave the host.</summary>
/// <remarks>
/// D-012 as a routing input: this is not advisory metadata, it is the value the model
/// router branches on, and <see cref="LocalOnly"/> can never reach a frontier provider.
/// </remarks>
public enum PrivacyClass
{
    /// <summary>Touches profile-derived content. Never leaves the host.</summary>
    LocalOnly = 0,

    /// <summary>Carries nothing profile-derived; a frontier provider may see it.</summary>
    Egressable = 1,
}
