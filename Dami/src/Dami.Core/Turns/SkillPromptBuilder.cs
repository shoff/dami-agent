using Dami.Contracts.Capabilities;
using Microsoft.Extensions.Options;

namespace Dami.Core.Turns;

/// <summary>Loads and renders selected procedures under one hard prompt budget.</summary>
public sealed class SkillPromptBuilder : ISkillPromptBuilder
{
    private const int MAX_PROMPT_CHARACTERS = 65_536;
    private const int MAX_SKILLS = 64;
    private const string HEADER = "\nSelected skill procedures (follow when relevant):\n";
    private const string PREFIX = "--- skill: ";
    private const string ID_OPEN = " [";
    private const string VERSION_SEPARATOR = "@";
    private const string ID_CLOSE = "] ---\n";
    private const string SUFFIX = "\n--- end skill ---\n";

    private readonly ISkillContentReader contentReader;
    private readonly int maxPromptCharacters;
    private readonly int maxSkills;

    /// <summary>Creates a skill prompt builder with snapshotted hard limits.</summary>
    public SkillPromptBuilder(
        ISkillContentReader contentReader,
        IOptions<SkillPromptOptions> options)
    {
        ArgumentNullException.ThrowIfNull(contentReader);
        ArgumentNullException.ThrowIfNull(options);
        SkillPromptOptions snapshot = options.Value;
        ArgumentOutOfRangeException.ThrowIfLessThan(snapshot.MaxPromptCharacters, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            snapshot.MaxPromptCharacters, MAX_PROMPT_CHARACTERS);
        ArgumentOutOfRangeException.ThrowIfLessThan(snapshot.MaxSkills, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(snapshot.MaxSkills, MAX_SKILLS);
        this.contentReader = contentReader;
        this.maxPromptCharacters = snapshot.MaxPromptCharacters;
        this.maxSkills = snapshot.MaxSkills;
    }

    /// <summary>Reads and renders only the selected skill bodies.</summary>
    public async Task<string> BuildAsync(
        IReadOnlyList<SkillSelection> skills,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(skills);
        if (skills.Count == 0)
        {
            return string.Empty;
        }

        this.ValidateSelectionCount(skills.Count);
        LoadedSkills loaded = await this.LoadAsync(skills, cancellationToken).ConfigureAwait(false);
        return string.Create(loaded.CharacterCount, loaded.Items, RenderInto);
    }

    private async Task<LoadedSkills> LoadAsync(
        IReadOnlyList<SkillSelection> skills,
        CancellationToken cancellationToken)
    {
        var loaded = new LoadedSkill[skills.Count];
        var characterCount = HEADER.Length;
        for (var index = 0; index < skills.Count; index++)
        {
            SkillSelection skill = skills[index]
                ?? throw new ArgumentException("Skill selections cannot contain null.", nameof(skills));
            characterCount = checked(characterCount + RenderedMetadataLength(skill));
            this.EnsureWithinBudget(characterCount);
            string body = await this.contentReader.ReadBodyAsync(
                skill.CapabilityId, skill.Version, cancellationToken).ConfigureAwait(false);
            loaded[index] = new LoadedSkill(skill, body);
            characterCount = checked(characterCount + body.Length);
            this.EnsureWithinBudget(characterCount);
        }

        return new LoadedSkills(loaded, characterCount);
    }

    private void ValidateSelectionCount(int count)
    {
        if (count > this.maxSkills)
        {
            throw new InvalidDataException(
                $"Selected skills exceed the configured bound of {this.maxSkills}.");
        }
    }

    private void EnsureWithinBudget(int characterCount)
    {
        if (characterCount > this.maxPromptCharacters)
        {
            throw new InvalidDataException(
                $"Selected skill procedures exceed the {this.maxPromptCharacters}-character prompt bound.");
        }
    }

    private static void RenderInto(Span<char> destination, LoadedSkill[] items)
    {
        var offset = 0;
        Copy(HEADER, destination, ref offset);
        for (var index = 0; index < items.Length; index++)
        {
            RenderOne(destination, items[index], ref offset);
        }
    }

    private static void RenderOne(
        Span<char> destination,
        LoadedSkill item,
        ref int offset)
    {
        SkillSelection skill = item.Selection;
        Copy(PREFIX, destination, ref offset);
        Copy(skill.Name, destination, ref offset);
        Copy(ID_OPEN, destination, ref offset);
        if (!skill.CapabilityId.TryFormat(destination[offset..], out int idCharacters, "D"))
        {
            throw new InvalidOperationException("The skill identifier did not fit its measured span.");
        }

        offset += idCharacters;
        Copy(VERSION_SEPARATOR, destination, ref offset);
        Copy(skill.Version, destination, ref offset);
        Copy(ID_CLOSE, destination, ref offset);
        Copy(item.Body, destination, ref offset);
        Copy(SUFFIX, destination, ref offset);
    }

    private static int RenderedMetadataLength(SkillSelection skill)
    {
        return checked(
            PREFIX.Length
            + skill.Name.Length
            + ID_OPEN.Length
            + 36
            + VERSION_SEPARATOR.Length
            + skill.Version.Length
            + ID_CLOSE.Length
            + SUFFIX.Length);
    }

    private static void Copy(string source, Span<char> destination, ref int offset)
    {
        source.AsSpan().CopyTo(destination[offset..]);
        offset += source.Length;
    }

    private sealed record LoadedSkill(SkillSelection Selection, string Body);

    private sealed record LoadedSkills(LoadedSkill[] Items, int CharacterCount);
}
