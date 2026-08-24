using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Dami.Capabilities.Skills;

internal sealed class SkillVersionHash : IDisposable
{
    private const int CHARACTER_CHUNK = 256;

    private static readonly UTF8Encoding strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    public void AppendDescriptor(SkillDescriptor descriptor)
    {
        Span<byte> id = stackalloc byte[16];
        descriptor.Id.TryWriteBytes(id);
        this.Append(id);
        this.AppendText(descriptor.Name!);
        this.AppendText(descriptor.Description!);
        this.AppendStrings(descriptor.Tags!);
        this.AppendGuids(descriptor.RelatedCapabilities!);
        this.AppendStrings(descriptor.References!);
    }

    public void AppendContent(ReadOnlySpan<byte> content)
    {
        this.Append(content);
    }

    public void AppendText(string content)
    {
        int byteCount = strictUtf8.GetByteCount(content);
        this.AppendCount(byteCount);
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

    public string Complete()
    {
        return Convert.ToHexStringLower(this.hash.GetHashAndReset());
    }

    public void Dispose()
    {
        this.hash.Dispose();
    }

    private void AppendStrings(IReadOnlyList<string> values)
    {
        this.AppendCount(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            this.AppendText(values[index]);
        }
    }

    private void AppendGuids(IReadOnlyList<Guid> values)
    {
        this.AppendCount(values.Count);
        Span<byte> bytes = stackalloc byte[16];
        for (var index = 0; index < values.Count; index++)
        {
            values[index].TryWriteBytes(bytes);
            this.Append(bytes);
        }
    }

    private void AppendCount(int count)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, count);
        this.hash.AppendData(bytes);
    }

    private void Append(ReadOnlySpan<byte> bytes)
    {
        this.AppendCount(bytes.Length);
        this.hash.AppendData(bytes);
    }
}
