using System.Text;
using System.Text.Json;
using Dami.Contracts.Capabilities;

namespace Dami.Capabilities.Skills;

internal sealed class SkillDocumentWriter
{
    public const string VERSION_FILE = ".dami-version";

    private static readonly UTF8Encoding strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly int maxBodyBytes;
    private readonly int maxDescriptorBytes;
    private readonly int maxReferenceBytes;
    private readonly int maxReferences;

    public SkillDocumentWriter(SkillLoaderOptions options)
    {
        this.maxBodyBytes = options.MaxBodyBytes;
        this.maxDescriptorBytes = options.MaxDescriptorBytes;
        this.maxReferenceBytes = options.MaxReferenceBytes;
        this.maxReferences = options.MaxReferences;
    }

    public async Task WriteAsync(
        string directory,
        SkillDocument document,
        CancellationToken cancellationToken)
    {
        SkillDescriptor descriptor = SkillDocumentVersioner.CreateDescriptor(document);
        SkillDescriptorValidator.Validate(descriptor, this.maxReferences);
        this.ValidateContent(document, descriptor);
        Directory.CreateDirectory(directory);
        await this.WriteDescriptorAsync(
            Path.Combine(directory, "skill.json"), descriptor, cancellationToken)
            .ConfigureAwait(false);
        await WriteTextAsync(
            Path.Combine(directory, "SKILL.md"), document.Body, cancellationToken)
            .ConfigureAwait(false);
        await this.WriteReferencesAsync(
            directory, document, descriptor.References!, cancellationToken)
            .ConfigureAwait(false);
    }

    public static Task WriteVersionAsync(
        string directory,
        string version,
        CancellationToken cancellationToken)
    {
        return WriteTextAsync(
            Path.Combine(directory, VERSION_FILE), version, cancellationToken);
    }

    public static async Task WriteDurableTextAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        string temporary = string.Concat(path, ".tmp");
        try
        {
            File.Delete(temporary);
            await WriteTextAsync(temporary, content, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private void ValidateContent(SkillDocument document, SkillDescriptor descriptor)
    {
        if (strictUtf8.GetByteCount(document.Body) > this.maxBodyBytes)
        {
            throw new InvalidDataException("Skill body exceeds its configured byte bound.");
        }

        var total = 0;
        foreach (string reference in descriptor.References!)
        {
            total = checked(total + strictUtf8.GetByteCount(document.References[reference]));
            if (total > this.maxReferenceBytes)
            {
                throw new InvalidDataException("Skill references exceed their combined byte bound.");
            }
        }
    }

    private async Task WriteDescriptorAsync(
        string path,
        SkillDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        await using var stream = CreateStream(path);
        await JsonSerializer.SerializeAsync(stream, descriptor, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (stream.Length > this.maxDescriptorBytes)
        {
            throw new InvalidDataException("Skill descriptor exceeds its configured byte bound.");
        }

        stream.Flush(flushToDisk: true);
    }

    private async Task WriteReferencesAsync(
        string directory,
        SkillDocument document,
        IReadOnlyList<string> references,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < references.Count; index++)
        {
            string reference = references[index];
            string path = ResolveReference(directory, reference);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await WriteTextAsync(path, document.References[reference], cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task WriteTextAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        await using var stream = CreateStream(path);
        await using var writer = new StreamWriter(stream, strictUtf8, leaveOpen: true);
        await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static FileStream CreateStream(string path)
    {
        return new FileStream(
            path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
    }

    private static string ResolveReference(string directory, string reference)
    {
        string path = Path.GetFullPath(reference, directory);
        string relative = Path.GetRelativePath(directory, path);
        if (Path.IsPathRooted(reference)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Skill reference '{reference}' escapes its skill folder.");
        }

        return path;
    }
}
