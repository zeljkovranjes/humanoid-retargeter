// Bounded LZ4 block decoding, including the chained 64K dictionary used by binary KV3 blobs.
#nullable enable
using System;
using System.IO;

namespace HumanoidRetargeter.EditorTools.Embedded.Compression;

internal sealed class Lz4Chain : IDisposable
{
    byte[] history = Array.Empty<byte>();
    public Lz4Chain(int frameSize, int unused) { }
    public bool DecodeAndDrain(ReadOnlySpan<byte> input, Span<byte> output, out int decoded)
    {
        decoded = Lz4.Decode(input, output, history);
        var length = Math.Min(65536, history.Length + decoded);
        var next = new byte[length];
        var copied = Math.Min(decoded, length);
        history.AsSpan(history.Length - (length - copied)).CopyTo(next);
        output.Slice(decoded - copied, copied).CopyTo(next.AsSpan(length - copied));
        history = next;
        return true;
    }
    public void Dispose() { }
}
