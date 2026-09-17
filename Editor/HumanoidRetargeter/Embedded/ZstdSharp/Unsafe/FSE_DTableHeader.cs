#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace HumanoidRetargeterZstd.Unsafe
{
    /* ======    Decompression    ====== */
    public struct FSE_DTableHeader
    {
        public ushort tableLog;
        public ushort fastMode;
    }
}