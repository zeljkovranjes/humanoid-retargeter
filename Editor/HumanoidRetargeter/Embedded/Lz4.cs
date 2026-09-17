// Bounded LZ4 block decoding, including the chained 64K dictionary used by binary KV3 blobs.
#nullable enable
using System;
using System.IO;

namespace HumanoidRetargeterCompression;

internal static class Lz4
{
    internal static int Decode(ReadOnlySpan<byte> input, Span<byte> output, ReadOnlySpan<byte> history = default)
    {
        int read = 0, written = 0;
        while (read < input.Length)
        {
            var token = input[read++];
            var literals = Length(input, ref read, token >> 4);
            if (literals > input.Length - read || literals > output.Length - written) throw new InvalidDataException("Invalid LZ4 literal length.");
            input.Slice(read, literals).CopyTo(output[written..]); read += literals; written += literals;
            if (read == input.Length) break;
            if (input.Length - read < 2) throw new InvalidDataException("Truncated LZ4 match.");
            var distance = input[read] | input[read + 1] << 8; read += 2;
            var length = checked(Length(input, ref read, token & 15) + 4);
            if (distance == 0 || distance > written + history.Length || length > output.Length - written) throw new InvalidDataException("Invalid LZ4 match.");
            for (var i = 0; i < length; i++)
            {
                var from = written - distance;
                output[written++] = from < 0 ? history[history.Length + from] : output[from];
            }
        }
        return written;
    }

    static int Length(ReadOnlySpan<byte> input, ref int read, int value)
    {
        if (value != 15) return value;
        byte next;
        do
        {
            if (read == input.Length) throw new InvalidDataException("Truncated LZ4 length.");
            next = input[read++]; value = checked(value + next);
        } while (next == 255);
        return value;
    }
}

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
