#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace HumanoidRetargeter.EditorTools.Embedded.ZstdSharp.Unsafe;

    /* Hashset for storing references to multiple ZSTD_DDict within ZSTD_DCtx */
    public unsafe struct ZSTD_DDictHashSet
    {
        public ZSTD_DDict_s** ddictPtrTable;
        public nuint ddictPtrTableSize;
        public nuint ddictPtrCount;
    }
