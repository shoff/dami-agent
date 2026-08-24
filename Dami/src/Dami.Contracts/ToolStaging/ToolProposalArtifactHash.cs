using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dami.Contracts.ToolStaging;

internal sealed class ToolProposalArtifactHash : IDisposable
{
    private const int CHARACTER_CHUNK = 256;
    private static readonly UTF8Encoding strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    public static string Compute(ToolProposalArtifact artifact)
    {
        using var hash = new ToolProposalArtifactHash();
        hash.AppendGuid(artifact.Schema.CapabilityId);
        hash.AppendText(artifact.Schema.Name);
        hash.AppendText(artifact.Schema.Description);
        hash.AppendJson(artifact.Schema.Parameters);
        hash.AppendStrings(artifact.Tags);
        hash.AppendFiles(artifact.SourceFiles);
        hash.AppendFiles(artifact.TestFiles);
        hash.AppendText(artifact.Rationale);
        hash.AppendGuids(artifact.ObservationIds);
        hash.AppendInt((int)artifact.ExecutionProfile);
        return hash.Complete();
    }

    public void Dispose()
    {
        this.hash.Dispose();
    }

    private void AppendFiles(IReadOnlyDictionary<string, string> files)
    {
        string[] paths = files.Keys.ToArray();
        Array.Sort(paths, StringComparer.Ordinal);
        this.AppendInt(paths.Length);
        for (var index = 0; index < paths.Length; index++)
        {
            this.AppendText(paths[index]);
            this.AppendText(files[paths[index]]);
        }
    }

    private void AppendStrings(IReadOnlyList<string> values)
    {
        this.AppendInt(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            this.AppendText(values[index]);
        }
    }

    private void AppendGuids(IReadOnlyList<Guid> values)
    {
        this.AppendInt(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            this.AppendGuid(values[index]);
        }
    }

    private void AppendGuid(Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        value.TryWriteBytes(bytes);
        this.Append(bytes);
    }

    private void AppendJson(JsonElement element)
    {
        this.AppendInt((int)element.ValueKind);
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                this.AppendJsonObject(element);
                return;
            case JsonValueKind.Array:
                this.AppendJsonArray(element);
                return;
            case JsonValueKind.String:
                this.AppendText(element.GetString()!);
                return;
            case JsonValueKind.Number:
                this.AppendText(element.GetRawText());
                return;
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                return;
            default:
                throw new ArgumentException("Tool parameter schemas cannot be undefined.", nameof(element));
        }
    }

    private void AppendJsonObject(JsonElement element)
    {
        JsonProperty[] properties = element.EnumerateObject().ToArray();
        Array.Sort(properties, static (left, right) =>
            string.Compare(left.Name, right.Name, StringComparison.Ordinal));
        this.AppendInt(properties.Length);
        for (var index = 0; index < properties.Length; index++)
        {
            this.AppendText(properties[index].Name);
            this.AppendJson(properties[index].Value);
        }
    }

    private void AppendJsonArray(JsonElement element)
    {
        this.AppendInt(element.GetArrayLength());
        foreach (JsonElement item in element.EnumerateArray())
        {
            this.AppendJson(item);
        }
    }

    private void AppendInt(int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        this.hash.AppendData(bytes);
    }

    private void AppendText(string content)
    {
        int byteCount = strictUtf8.GetByteCount(content);
        this.AppendInt(byteCount);
        ReadOnlySpan<char> remaining = content.AsSpan();
        Span<byte> bytes = stackalloc byte[CHARACTER_CHUNK * 3];
        while (!remaining.IsEmpty)
        {
            int characters = Math.Min(CHARACTER_CHUNK, remaining.Length);
            if (characters < remaining.Length && char.IsHighSurrogate(remaining[characters - 1]))
            {
                characters--;
            }

            int written = strictUtf8.GetBytes(remaining[..characters], bytes);
            this.hash.AppendData(bytes[..written]);
            remaining = remaining[characters..];
        }
    }

    private string Complete()
    {
        return Convert.ToHexStringLower(this.hash.GetHashAndReset());
    }

    private void Append(ReadOnlySpan<byte> bytes)
    {
        this.AppendInt(bytes.Length);
        this.hash.AppendData(bytes);
    }
}
