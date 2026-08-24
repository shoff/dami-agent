using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dami.Contracts.Capabilities;

namespace Dami.Capabilities.Skills;

/// <summary>Loads bounded local skill folders into the unified capability registry.</summary>
public sealed class SkillCapabilityLoader : ISkillContentReader
{
    private const int MAX_FILE_BYTES = 16 * 1024 * 1024;
    private const int MAX_SKILLS = 4096;
    private static readonly UTF8Encoding strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly ICapabilityBatchRegistrar registrar;
    private readonly string rootDirectory;
    private readonly int maxSkills;
    private readonly int maxDescriptorBytes;
    private readonly int maxBodyBytes;
    private readonly int maxReferences;
    private readonly int maxReferenceBytes;
    private IReadOnlyDictionary<Guid, SkillSource> sources =
        new Dictionary<Guid, SkillSource>();

    /// <summary>Creates a snapshotted, bounded skill loader.</summary>
    public SkillCapabilityLoader(ICapabilityBatchRegistrar registrar, SkillLoaderOptions options)
    {
        ArgumentNullException.ThrowIfNull(registrar);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RootDirectory);
        ValidateBound(options.MaxSkills, MAX_SKILLS, nameof(options.MaxSkills));
        ValidateBound(options.MaxDescriptorBytes, MAX_FILE_BYTES, nameof(options.MaxDescriptorBytes));
        ValidateBound(options.MaxBodyBytes, MAX_FILE_BYTES, nameof(options.MaxBodyBytes));
        ValidateBound(options.MaxReferences, 1024, nameof(options.MaxReferences));
        ValidateBound(options.MaxReferenceBytes, MAX_FILE_BYTES, nameof(options.MaxReferenceBytes));
        this.registrar = registrar;
        this.rootDirectory = Path.GetFullPath(options.RootDirectory);
        this.maxSkills = options.MaxSkills;
        this.maxDescriptorBytes = options.MaxDescriptorBytes;
        this.maxBodyBytes = options.MaxBodyBytes;
        this.maxReferences = options.MaxReferences;
        this.maxReferenceBytes = options.MaxReferenceBytes;
    }

    /// <summary>Loads, validates, versions, then publishes every direct child skill.</summary>
    public async Task<IReadOnlyList<CapabilityEntry>> LoadAsync(
        DateTimeOffset registeredAt,
        CancellationToken cancellationToken)
    {
        string[] directories = this.FindDirectories();
        var loaded = new LoadedSkill[directories.Length];
        var entries = new CapabilityEntry[directories.Length];
        for (var index = 0; index < directories.Length; index++)
        {
            loaded[index] = await this.LoadOneAsync(
                directories[index], registeredAt, cancellationToken).ConfigureAwait(false);
            entries[index] = loaded[index].Entry;
        }

        this.registrar.RegisterBatch(entries);
        var replacement = new Dictionary<Guid, SkillSource>(loaded.Length);
        for (var index = 0; index < loaded.Length; index++)
        {
            replacement.Add(loaded[index].Entry.CapabilityId, loaded[index].Source);
        }

        Volatile.Write(ref this.sources, replacement);
        return Array.AsReadOnly(entries);
    }

    /// <inheritdoc />
    public async Task<string> ReadBodyAsync(
        Guid skillId,
        string expectedVersion,
        CancellationToken cancellationToken)
    {
        SkillSource source = this.FindSource(skillId, expectedVersion);
        byte[] bytes = await ReadBoundedAsync(
            Path.Combine(source.Directory, "SKILL.md"), this.maxBodyBytes, cancellationToken)
            .ConfigureAwait(false);
        ValidateUtf8(bytes);
        ValidateFingerprint(skillId, source.BodyFingerprint, bytes);
        return strictUtf8.GetString(bytes);
    }

    /// <inheritdoc />
    public async Task<string> ReadReferenceAsync(
        Guid skillId,
        string expectedVersion,
        string relativePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        SkillSource source = this.FindSource(skillId, expectedVersion);
        if (!source.ReferenceFingerprints.TryGetValue(
            relativePath, out byte[]? expectedFingerprint))
        {
            throw new InvalidDataException(
                $"Skill '{skillId}' does not declare reference '{relativePath}'.");
        }

        string path = ResolveReference(source.Directory, relativePath);
        byte[] bytes = await ReadBoundedAsync(path, this.maxReferenceBytes, cancellationToken)
            .ConfigureAwait(false);
        ValidateUtf8(bytes);
        ValidateFingerprint(skillId, expectedFingerprint, bytes);
        return strictUtf8.GetString(bytes);
    }

    private async Task<LoadedSkill> LoadOneAsync(
        string directory,
        DateTimeOffset registeredAt,
        CancellationToken cancellationToken)
    {
        EnsureOrdinaryDirectory(directory);
        byte[] descriptorBytes = await ReadBoundedAsync(
            Path.Combine(directory, "skill.json"), this.maxDescriptorBytes, cancellationToken)
            .ConfigureAwait(false);
        SkillDescriptor descriptor = JsonSerializer.Deserialize<SkillDescriptor>(descriptorBytes)
            ?? throw new InvalidDataException("Skill descriptor cannot be JSON null.");
        SkillDescriptorValidator.Validate(descriptor, this.maxReferences);
        byte[] bodyBytes = await ReadBoundedAsync(
            Path.Combine(directory, "SKILL.md"), this.maxBodyBytes, cancellationToken)
            .ConfigureAwait(false);
        ValidateUtf8(bodyBytes);
        VersionedContent content = await this.ComputeVersionAsync(
            directory, descriptor, bodyBytes, cancellationToken)
            .ConfigureAwait(false);
        return new LoadedSkill(
            CreateEntry(descriptor, content.Version, registeredAt),
            new SkillSource(
                directory,
                content.Version,
                content.BodyFingerprint,
                content.ReferenceFingerprints));
    }

    private SkillSource FindSource(Guid skillId, string expectedVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedVersion);
        IReadOnlyDictionary<Guid, SkillSource> snapshot = Volatile.Read(ref this.sources);
        if (!snapshot.TryGetValue(skillId, out SkillSource? source))
        {
            throw new KeyNotFoundException($"Skill '{skillId}' has not been published.");
        }

        if (!string.Equals(source.Version, expectedVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Skill '{skillId}' no longer matches the selected version.");
        }

        return source;
    }

    private async Task<VersionedContent> ComputeVersionAsync(
        string directory,
        SkillDescriptor descriptor,
        byte[] bodyBytes,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendDescriptor(hash, descriptor);
        Append(hash, bodyBytes);
        var totalReferenceBytes = 0;
        var referenceFingerprints = new Dictionary<string, byte[]>(
            descriptor.References!.Length, StringComparer.Ordinal);
        foreach (string reference in descriptor.References!)
        {
            string path = ResolveReference(directory, reference);
            int remaining = this.maxReferenceBytes - totalReferenceBytes;
            byte[] bytes = await ReadBoundedAsync(path, remaining, cancellationToken)
                .ConfigureAwait(false);
            totalReferenceBytes += bytes.Length;
            Append(hash, bytes);
            referenceFingerprints.Add(reference, SHA256.HashData(bytes));
        }

        return new VersionedContent(
            Convert.ToHexStringLower(hash.GetHashAndReset()),
            SHA256.HashData(bodyBytes),
            referenceFingerprints);
    }

    private static void AppendDescriptor(IncrementalHash hash, SkillDescriptor descriptor)
    {
        Span<byte> id = stackalloc byte[16];
        descriptor.Id.TryWriteBytes(id);
        Append(hash, id);
        AppendString(hash, descriptor.Name!);
        AppendString(hash, descriptor.Description!);
        AppendStrings(hash, descriptor.Tags!);
        AppendGuids(hash, descriptor.RelatedCapabilities!);
        AppendStrings(hash, descriptor.References!);
    }

    private static void AppendStrings(IncrementalHash hash, IReadOnlyList<string> values)
    {
        AppendCount(hash, values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            AppendString(hash, values[index]);
        }
    }

    private static void AppendGuids(IncrementalHash hash, IReadOnlyList<Guid> values)
    {
        AppendCount(hash, values.Count);
        Span<byte> bytes = stackalloc byte[16];
        for (var index = 0; index < values.Count; index++)
        {
            values[index].TryWriteBytes(bytes);
            Append(hash, bytes);
        }
    }

    private static void AppendString(IncrementalHash hash, string value)
    {
        int byteCount = strictUtf8.GetByteCount(value);
        if (byteCount <= 512)
        {
            Span<byte> bytes = stackalloc byte[byteCount];
            strictUtf8.GetBytes(value, bytes);
            Append(hash, bytes);
            return;
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            int written = strictUtf8.GetBytes(value, rented);
            Append(hash, rented.AsSpan(0, written));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void AppendCount(IncrementalHash hash, int count)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, count);
        hash.AppendData(bytes);
    }

    private string[] FindDirectories()
    {
        if (!Directory.Exists(this.rootDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Skill root '{this.rootDirectory}' does not exist.");
        }

        EnsureOrdinaryDirectory(this.rootDirectory);
        var directories = new List<string>();
        foreach (string directory in Directory.EnumerateDirectories(this.rootDirectory))
        {
            if (directories.Count == this.maxSkills)
            {
                throw new InvalidDataException(
                    $"Skill root exceeds its bound of {this.maxSkills} folders.");
            }

            directories.Add(directory);
        }

        directories.Sort(StringComparer.Ordinal);
        return directories.ToArray();
    }

    private static CapabilityEntry CreateEntry(
        SkillDescriptor descriptor,
        string version,
        DateTimeOffset registeredAt)
    {
        return new CapabilityEntry(
            descriptor.Id,
            descriptor.Name!,
            descriptor.Description!,
            CapabilityKind.Skill,
            CapabilitySource.Skill,
            TrustLevel.Trusted,
            descriptor.Tags!,
            null,
            $"skill://{descriptor.Id:D}/SKILL.md",
            descriptor.RelatedCapabilities!,
            version,
            registeredAt);
    }

    private static async Task<byte[]> ReadBoundedAsync(
        string path,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        if (maxBytes < 0)
        {
            throw new InvalidDataException("Combined skill reference content exceeds its byte bound.");
        }

        EnsureOrdinaryFile(path);
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > maxBytes)
        {
            throw new InvalidDataException($"Skill file '{Path.GetFileName(path)}' exceeds its byte bound.");
        }

        var bytes = GC.AllocateUninitializedArray<byte>(checked((int)stream.Length));
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        var overflow = new byte[1];
        if (await stream.ReadAsync(overflow, cancellationToken).ConfigureAwait(false) != 0)
        {
            throw new InvalidDataException($"Skill file '{Path.GetFileName(path)}' grew beyond its byte bound.");
        }

        return bytes;
    }

    private static string ResolveReference(string directory, string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        string path = Path.GetFullPath(reference, directory);
        string relative = Path.GetRelativePath(directory, path);
        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Skill reference '{reference}' escapes its skill folder.");
        }

        EnsureNoLinkedParent(directory, path, reference);
        return path;
    }

    private static void EnsureNoLinkedParent(
        string directory,
        string path,
        string reference)
    {
        DirectoryInfo? parent = new FileInfo(path).Directory;
        while (parent is not null
            && !string.Equals(parent.FullName, directory, StringComparison.Ordinal))
        {
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    $"Skill reference '{reference}' crosses a symbolic link.");
            }

            parent = parent.Parent;
        }
    }

    private static void EnsureOrdinaryDirectory(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Symbolic-link skill folders are not allowed.");
        }
    }

    private static void EnsureOrdinaryFile(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw new InvalidDataException($"Skill file '{Path.GetFileName(path)}' is not an ordinary file.");
        }
    }

    private static void Append(IncrementalHash hash, ReadOnlySpan<byte> bytes)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static void ValidateUtf8(ReadOnlySpan<byte> bytes)
    {
        try
        {
            strictUtf8.GetCharCount(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Skill body must contain valid UTF-8.", exception);
        }
    }

    private static void ValidateFingerprint(
        Guid skillId,
        ReadOnlySpan<byte> expected,
        ReadOnlySpan<byte> content)
    {
        Span<byte> actual = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(content, actual);
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            throw new InvalidDataException(
                $"Skill '{skillId}' content no longer matches its published version.");
        }
    }

    private static void ValidateBound(int value, int maximum, string parameterName)
    {
        if (value is < 1 || value > maximum)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private sealed record LoadedSkill(CapabilityEntry Entry, SkillSource Source);

    private sealed record SkillSource(
        string Directory,
        string Version,
        byte[] BodyFingerprint,
        IReadOnlyDictionary<string, byte[]> ReferenceFingerprints);

    private sealed record VersionedContent(
        string Version,
        byte[] BodyFingerprint,
        IReadOnlyDictionary<string, byte[]> ReferenceFingerprints);
}
