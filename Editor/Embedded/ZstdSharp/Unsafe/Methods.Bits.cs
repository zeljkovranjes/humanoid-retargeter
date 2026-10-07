#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using static HumanoidRetargeter.EditorTools.Embedded.ZstdSharp.UnsafeHelper;
using System.Numerics;

namespace HumanoidRetargeter.EditorTools.Embedded.ZstdSharp.Unsafe;

    public static unsafe partial class Methods
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ZSTD_countTrailingZeros32(uint val)
        {
            assert(val != 0);
            return (uint)BitOperations.TrailingZeroCount(val);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ZSTD_countTrailingZeros64(ulong val)
        {
            assert(val != 0);
            return (uint)BitOperations.TrailingZeroCount(val);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
private static uint ZSTD_highbit32(uint val)
        {
            assert(val != 0);
            return (uint)BitOperations.Log2(val);
        }
    }
