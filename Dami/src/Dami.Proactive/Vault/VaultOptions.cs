namespace Dami.Proactive.Vault;

/// <summary>Where the Markdown vault lives.</summary>
public sealed class VaultOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SECTION = "Vault";

    /// <summary>The vault folder; open it in Obsidian, or version it with git.</summary>
    public string Directory { get; set; } = "/home/steve/Data/dami-vault";

    /// <summary>The zone "yesterday" is read in.</summary>
    public string TimeZone { get; set; } = "America/Chicago";
}
