using System.Security.Cryptography;
using OpenIddict.Abstractions;

namespace Dami.Authentication;

/// <summary>Creates narrowly scoped first-party client registrations.</summary>
public sealed class DamiClientProvisioner(IOpenIddictApplicationManager applications)
{
    /// <summary>
    /// Ensures the two first-party public clients exist. Idempotent: a host that restarts
    /// must not fail, and must not mint a second registration for the same client id.
    /// </summary>
    /// <remarks>
    /// Without this nothing can authenticate at all. The profiles, the endpoints and the
    /// policies were all built and tested, but no code ever created the dami-cli or
    /// dami-gui registrations outside a test fixture — so every flow would have failed at
    /// the first request with an unknown client.
    ///
    /// Public clients, so no secret is generated or stored. The CLI proves itself with the
    /// device flow and the GUI with PKCE; neither can keep a secret on a machine its user
    /// controls, and pretending otherwise is how a "confidential" client ends up with its
    /// password in a config file.
    /// </remarks>
    public async Task<IReadOnlyList<string>> EnsureFirstPartyClientsAsync(
        Uri guiRedirectUri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(guiRedirectUri);

        var created = new List<string>();
        foreach (var descriptor in new[] { DamiClientProfiles.Cli(), DamiClientProfiles.Gui(guiRedirectUri) })
        {
            var clientId = descriptor.ClientId!;
            if (await applications.FindByClientIdAsync(clientId, cancellationToken)
                .ConfigureAwait(false) is not null)
            {
                continue;
            }

            await applications.CreateAsync(descriptor, cancellationToken).ConfigureAwait(false);
            created.Add(clientId);
        }

        return created;
    }

    /// <summary>Enrolls one confidential service and returns its generated secret once.</summary>
    public async Task<string> EnrollServiceAsync(
        string clientId,
        string displayName,
        IReadOnlyCollection<string> scopes,
        CancellationToken cancellationToken)
    {
        if (await applications.FindByClientIdAsync(clientId, cancellationToken)
            .ConfigureAwait(false) is not null)
        {
            throw new InvalidOperationException($"Client '{clientId}' is already enrolled.");
        }

        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        OpenIddictApplicationDescriptor descriptor = DamiClientProfiles.Service(
            clientId, displayName, scopes);
        descriptor.ClientSecret = secret;
        await applications.CreateAsync(descriptor, cancellationToken).ConfigureAwait(false);
        return secret;
    }
}
