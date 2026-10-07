#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace HumanoidRetargeter.EditorTools.Embedded.ZstdSharp.Unsafe;

    /* ======    Decompression    ====== */
    public struct FSE_DTableHeader
    {
        public ushort tableLog;
        public ushort fastMode;
    }
