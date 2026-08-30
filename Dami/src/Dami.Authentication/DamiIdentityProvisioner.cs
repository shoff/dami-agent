using Microsoft.AspNetCore.Identity;

namespace Dami.Authentication;

/// <summary>What ensuring the bootstrap identity came to.</summary>
public enum IdentityProvisionResult
{
    /// <summary>The account was created.</summary>
    Created,

    /// <summary>The account was already there and was left untouched.</summary>
    AlreadyExists,

    /// <summary>The store refused the account; the error says why.</summary>
    Failed,
}

/// <summary>One provisioning outcome, with the refusal when there is one.</summary>
public sealed record IdentityProvision(IdentityProvisionResult Result, string? Error);

/// <summary>Creates the single human account the login flows authenticate against.</summary>
/// <remarks>
/// The counterpart to <see cref="DamiClientProvisioner"/>, closing the same shape of gap:
/// the endpoints check passwords against the identity store, the clients are registered,
/// and nothing ever created a user — so with authentication enabled, every login on every
/// client would fail at the password check with the whole stack green around it.
///
/// Idempotent by name, and deliberately not an upsert: re-running with a different
/// configured password must not quietly reset the account's real one.
/// </remarks>
public sealed class DamiIdentityProvisioner
{
    private readonly UserManager<DamiIdentity> users;

    /// <summary>Creates the provisioner.</summary>
    public DamiIdentityProvisioner(UserManager<DamiIdentity> users)
    {
        ArgumentNullException.ThrowIfNull(users);
        this.users = users;
    }

    /// <summary>Creates the account if it does not exist; never alters one that does.</summary>
    public async Task<IdentityProvision> EnsureIdentityAsync(
        string username, string password, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrEmpty(password);
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await this.users.FindByNameAsync(username).ConfigureAwait(false);
        if (existing is not null)
        {
            return new IdentityProvision(IdentityProvisionResult.AlreadyExists, null);
        }

        var created = await this.users
            .CreateAsync(new DamiIdentity { UserName = username }, password)
            .ConfigureAwait(false);
        return created.Succeeded
            ? new IdentityProvision(IdentityProvisionResult.Created, null)
            : new IdentityProvision(IdentityProvisionResult.Failed, Describe(created));
    }

    private static string Describe(IdentityResult result)
    {
        var reasons = new List<string>();
        foreach (var error in result.Errors)
        {
            reasons.Add(error.Description);
        }

        return reasons.Count > 0 ? string.Join("; ", reasons) : "the identity store said no";
    }
}
