#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace HumanoidRetargeter.EditorTools.Embedded.ZstdSharp.Unsafe;

    public unsafe struct ZSTD_outBuffer_s
    {
        /**< start of output buffer */
        public void* dst;
        /**< size of output buffer */
        public nuint size;
        /**< position where writing stopped. Will be updated. Necessarily 0 <= pos <= size */
        public nuint pos;
    }
