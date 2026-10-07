#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace HumanoidRetargeter.EditorTools.Embedded.ZstdSharp.Unsafe;

    /*-*******************************************************
     *  Decompression types
     *********************************************************/
    public struct ZSTD_seqSymbol_header
    {
        public uint fastMode;
        public uint tableLog;
    }
