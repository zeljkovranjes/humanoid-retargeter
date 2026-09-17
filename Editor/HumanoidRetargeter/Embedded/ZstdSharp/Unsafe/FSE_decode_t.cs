#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace HumanoidRetargeterZstd.Unsafe
{
    public struct FSE_decode_t
    {
        public ushort newState;
        public byte symbol;
        public byte nbBits;
    }
}