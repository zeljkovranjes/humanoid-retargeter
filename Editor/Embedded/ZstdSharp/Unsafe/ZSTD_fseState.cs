#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace HumanoidRetargeter.EditorTools.Embedded.ZstdSharp.Unsafe;

    public unsafe struct ZSTD_fseState
    {
        public nuint state;
        public ZSTD_seqSymbol* table;
    }
