#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace HumanoidRetargeter.EditorTools.Embedded.ZstdSharp.Unsafe;

    public static unsafe partial class Methods
    {
        private static readonly ZSTD_customMem ZSTD_defaultCMem = new ZSTD_customMem(customAlloc: null, customFree: null, opaque: null);
    }
