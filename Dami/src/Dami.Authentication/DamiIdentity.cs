using Microsoft.AspNetCore.Identity;

namespace Dami.Authentication;

/// <summary>A locally authorized human identity persisted outside Dami's domain schema.</summary>
public sealed class DamiIdentity : IdentityUser<Guid>
{
}
