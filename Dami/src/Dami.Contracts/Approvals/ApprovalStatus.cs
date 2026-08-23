namespace Dami.Contracts.Approvals;

/// <summary>Where an approval stands.</summary>
public enum ApprovalStatus
{
    /// <summary>Waiting for a human. The action is blocked.</summary>
    Pending = 0,

    /// <summary>A human said yes. The action may proceed, once.</summary>
    Approved = 1,

    /// <summary>A human said no. The action never proceeds.</summary>
    Denied = 2,

    /// <summary>Nobody answered in time. Treated exactly like denial.</summary>
    Expired = 3,
}
