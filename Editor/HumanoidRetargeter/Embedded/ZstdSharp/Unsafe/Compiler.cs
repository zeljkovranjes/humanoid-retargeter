#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace HumanoidRetargeterZstd.Unsafe
{
    public static unsafe partial class Methods
    {

        /**
         * Helper function to perform a wrapped pointer add without triggering UBSAN.
         *
         * @return ptr + add with wrapping
         */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte* ZSTD_wrappedPtrAdd(byte* ptr, nint add)
        {
            return ptr + add;
        }

        /**
         * Helper function to perform a wrapped pointer subtraction without triggering
         * UBSAN.
         *
         * @return ptr - sub with wrapping
         */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte* ZSTD_wrappedPtrSub(byte* ptr, nint sub)
        {
            return ptr - sub;
        }

        /**
         * Helper function to add to a pointer that works around C's undefined behavior
         * of adding 0 to NULL.
         *
         * @returns `ptr + add` except it defines `NULL + 0 == NULL`.
         */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte* ZSTD_maybeNullPtrAdd(byte* ptr, nint add)
        {
            return add > 0 ? ptr + add : ptr;
        }
    }
}