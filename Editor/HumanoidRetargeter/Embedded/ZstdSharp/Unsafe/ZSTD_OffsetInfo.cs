#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace HumanoidRetargeterZstd.Unsafe
{
    public struct ZSTD_OffsetInfo
    {
        public uint longOffsetShare;
        public uint maxNbAdditionalBits;
    }
}