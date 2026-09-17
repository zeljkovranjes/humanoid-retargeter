#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System;

namespace HumanoidRetargeterDmx;

public record struct Color(byte R, byte G, byte B, byte A)
{
    public static Color FromBytes(ReadOnlySpan<byte> bytes)
        => new(bytes[0], bytes[1], bytes[2], bytes[3]);

    public void ToBytes(Span<byte> bytes)
    {
        bytes[0] = R; bytes[1] = G; bytes[2] = B; bytes[3] = A;
    }

    public byte[] ToBytes()
        => [R, G, B, A];
}
