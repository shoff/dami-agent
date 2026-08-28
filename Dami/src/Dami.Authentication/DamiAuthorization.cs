namespace Dami.Authentication;

/// <summary>OAuth scopes understood by the Dami runtime API.</summary>
public static class DamiAuthorizationScopes
{
    /// <summary>Allows reading private runtime state.</summary>
    public const string RUNTIME_READ = "dami.runtime.read";

    /// <summary>Allows ordinary runtime mutations.</summary>
    public const string RUNTIME_WRITE = "dami.runtime.write";

    /// <summary>Allows a user-authorized client to resolve pending approvals.</summary>
    public const string APPROVALS_RESOLVE = "dami.approvals.resolve";
}

/// <summary>ASP.NET Core authorization policies used by Dami endpoints.</summary>
public static class DamiAuthorizationPolicies
{
    /// <summary>Requires private runtime read authority.</summary>
    public const string RUNTIME_READ = "DamiRuntimeRead";

    /// <summary>Requires ordinary runtime mutation authority.</summary>
    public const string RUNTIME_WRITE = "DamiRuntimeWrite";

    /// <summary>Requires the dedicated approval-resolution scope.</summary>
    public const string APPROVALS_RESOLVE = "DamiApprovalsResolve";
}

/// <summary>Claims issued to Dami runtime clients.</summary>
public static class DamiAuthorizationClaims
{
    /// <summary>Identifies whether the subject is a human or an agent.</summary>
    public const string ACTOR_KIND = "dami.actor_kind";
}
