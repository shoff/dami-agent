namespace Dami.Capabilities.Skills;

internal sealed record SkillDirectoryIdentity(
    string Directory,
    Guid SkillId,
    string Version);
