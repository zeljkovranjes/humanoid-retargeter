#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using static HumanoidRetargeterZstd.UnsafeHelper;

namespace HumanoidRetargeterZstd.Unsafe
{
    public static unsafe partial class Methods
    {
        /* custom memory allocation functions */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void* ZSTD_customMalloc(nuint size, ZSTD_customMem customMem)
        {
            if (customMem.customAlloc != null)
                return ((delegate* managed<void*, nuint, void*>)customMem.customAlloc)(customMem.opaque, size);
            return malloc(size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ZSTD_customFree(void* ptr, ZSTD_customMem customMem)
        {
            if (ptr != null)
            {
                if (customMem.customFree != null)
                    ((delegate* managed<void*, void*, void>)customMem.customFree)(customMem.opaque, ptr);
                else
                    free(ptr);
            }
        }
    }
}