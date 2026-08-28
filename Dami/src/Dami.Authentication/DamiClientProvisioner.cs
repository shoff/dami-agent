using System.Security.Cryptography;
using OpenIddict.Abstractions;

namespace Dami.Authentication;

/// <summary>Creates narrowly scoped first-party client registrations.</summary>
public sealed class DamiClientProvisioner(IOpenIddictApplicationManager applications)
{
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
