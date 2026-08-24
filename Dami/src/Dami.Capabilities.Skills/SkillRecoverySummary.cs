namespace Dami.Capabilities.Skills;

/// <summary>Observed result of one bounded startup-recovery batch.</summary>
public readonly record struct SkillRecoverySummary(
    int Attempted,
    int Succeeded,
    int Failed);
