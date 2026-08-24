using System.Collections.ObjectModel;
using System.Text;
using Dami.Contracts.Capabilities;

namespace Dami.Contracts.ToolStaging;

/// <summary>An inert, complete source-and-test artifact submitted for human review.</summary>
public sealed record ToolProposalArtifact
{
    private const int MAX_FILE_SET_BYTES = 1_048_576;
    private const int MAX_FILES_PER_SET = 64;
    private const int MAX_TAGS = 32;
    private const int MAX_TAG_BYTES = 256;
    private const int MAX_RATIONALE_BYTES = 65_536;
    private const int MAX_OBSERVATIONS = 64;
    private const int MAX_PATH_BYTES = 240;
    private const int MAX_SCHEMA_DESCRIPTION_BYTES = 4_096;
    private const int MAX_SCHEMA_BYTES = 65_536;
    private static readonly UTF8Encoding strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Creates and snapshots one review artifact.</summary>
    public ToolProposalArtifact(
        CapabilityToolSchema schema,
        IReadOnlyList<string> tags,
        IReadOnlyDictionary<string, string> sourceFiles,
        IReadOnlyDictionary<string, string> testFiles,
        string rationale,
        IReadOnlyList<Guid> observationIds,
        ToolExecutionProfile executionProfile)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ValidateBoundedText(
            schema.Description, MAX_SCHEMA_DESCRIPTION_BYTES, nameof(schema));
        ValidateBoundedText(
            schema.Parameters.GetRawText(), MAX_SCHEMA_BYTES, nameof(schema));
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(sourceFiles);
        ArgumentNullException.ThrowIfNull(testFiles);
        ArgumentException.ThrowIfNullOrWhiteSpace(rationale);
        ValidateBoundedText(rationale, MAX_RATIONALE_BYTES, nameof(rationale));
        ArgumentNullException.ThrowIfNull(observationIds);
        ValidateProfile(executionProfile);
        this.Schema = schema;
        this.Tags = SnapshotTags(tags);
        this.SourceFiles = SnapshotFiles(sourceFiles, nameof(sourceFiles));
        this.TestFiles = SnapshotFiles(testFiles, nameof(testFiles));
        this.Rationale = rationale;
        this.ObservationIds = SnapshotObservations(observationIds);
        this.ExecutionProfile = executionProfile;
        this.Version = ToolProposalArtifactHash.Compute(this);
    }

    /// <summary>Gets the typed model-facing tool contract.</summary>
    public CapabilityToolSchema Schema { get; }

    /// <summary>Gets compact semantic-retrieval tags.</summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>Gets inert C# implementation files by relative path.</summary>
    public IReadOnlyDictionary<string, string> SourceFiles { get; }

    /// <summary>Gets inert C# test files by relative path.</summary>
    public IReadOnlyDictionary<string, string> TestFiles { get; }

    /// <summary>Gets why the proposed capability should exist.</summary>
    public string Rationale { get; }

    /// <summary>Gets observations that motivated the proposal.</summary>
    public IReadOnlyList<Guid> ObservationIds { get; }

    /// <summary>Gets the proposal's declared maximum execution authority.</summary>
    public ToolExecutionProfile ExecutionProfile { get; }

    /// <summary>Gets the stable lowercase SHA-256 of all review-relevant fields.</summary>
    public string Version { get; }

    private static IReadOnlyList<string> SnapshotTags(IReadOnlyList<string> tags)
    {
        if (tags.Count > MAX_TAGS)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tags), $"A tool proposal cannot contain more than {MAX_TAGS} tags.");
        }

        var snapshot = new string[tags.Count];
        for (var index = 0; index < tags.Count; index++)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tags[index]);
            ValidateBoundedText(tags[index], MAX_TAG_BYTES, nameof(tags));
            snapshot[index] = tags[index];
        }

        return Array.AsReadOnly(snapshot);
    }

    private static IReadOnlyDictionary<string, string> SnapshotFiles(
        IReadOnlyDictionary<string, string> files,
        string parameterName)
    {
        if (files.Count == 0)
        {
            throw new ArgumentException("A tool proposal requires at least one file.", parameterName);
        }

        if (files.Count > MAX_FILES_PER_SET)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, $"A tool proposal file set cannot exceed {MAX_FILES_PER_SET} files.");
        }

        var snapshot = new Dictionary<string, string>(files.Count, StringComparer.Ordinal);
        var totalBytes = 0;
        foreach (KeyValuePair<string, string> file in files)
        {
            ValidateCSharpPath(file.Key, parameterName);
            ArgumentException.ThrowIfNullOrWhiteSpace(file.Value);
            totalBytes = AddBoundedBytes(totalBytes, file.Value, parameterName);
            snapshot.Add(file.Key, file.Value);
        }

        return new ReadOnlyDictionary<string, string>(snapshot);
    }

    private static int AddBoundedBytes(int current, string content, string parameterName)
    {
        int bytes = CountUtf8Bytes(content, parameterName);
        if (bytes > MAX_FILE_SET_BYTES - current)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, $"Tool proposal file sets cannot exceed {MAX_FILE_SET_BYTES} bytes.");
        }

        return current + bytes;
    }

    private static void ValidateBoundedText(string content, int maxBytes, string parameterName)
    {
        if (CountUtf8Bytes(content, parameterName) > maxBytes)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, $"Tool proposal text cannot exceed {maxBytes} bytes.");
        }
    }

    private static int CountUtf8Bytes(string content, string parameterName)
    {
        try
        {
            return strictUtf8.GetByteCount(content);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                "Tool proposal text must contain valid Unicode.", parameterName, exception);
        }
    }

    private static void ValidateCSharpPath(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        if (path.Length > MAX_PATH_BYTES)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, $"Tool proposal paths cannot exceed {MAX_PATH_BYTES} bytes.");
        }

        if (Path.IsPathRooted(path)
            || path.AsSpan().IndexOfAny('\0', '\\') >= 0
            || !path.EndsWith(".cs", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Tool proposal files must be relative C# source paths.", parameterName);
        }

        string[] segments = path.Split('/');
        for (var index = 0; index < segments.Length; index++)
        {
            if (segments[index].Length == 0
                || segments[index] is "." or ".."
                || !IsSafeSegment(segments[index]))
            {
                throw new ArgumentException(
                    "Tool proposal paths cannot traverse or contain unsafe characters.", parameterName);
            }
        }
    }

    private static bool IsSafeSegment(string segment)
    {
        for (var index = 0; index < segment.Length; index++)
        {
            char character = segment[index];
            if (!char.IsAsciiLetterOrDigit(character) && character is not '.' and not '_' and not '-')
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<Guid> SnapshotObservations(IReadOnlyList<Guid> observationIds)
    {
        if (observationIds.Count == 0)
        {
            throw new ArgumentException(
                "A tool proposal requires motivating observations.", nameof(observationIds));
        }
        if (observationIds.Count > MAX_OBSERVATIONS)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observationIds),
                $"A tool proposal cannot cite more than {MAX_OBSERVATIONS} observations.");
        }

        var snapshot = new Guid[observationIds.Count];
        for (var index = 0; index < observationIds.Count; index++)
        {
            if (observationIds[index] == Guid.Empty)
            {
                throw new ArgumentException(
                    "Motivating observation identifiers cannot be empty.", nameof(observationIds));
            }

            snapshot[index] = observationIds[index];
        }

        return Array.AsReadOnly(snapshot);
    }

    private static void ValidateProfile(ToolExecutionProfile executionProfile)
    {
        if (!Enum.IsDefined(executionProfile))
        {
            throw new ArgumentOutOfRangeException(nameof(executionProfile));
        }
    }
}
