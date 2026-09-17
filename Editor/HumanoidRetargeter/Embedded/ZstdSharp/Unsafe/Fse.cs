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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void FSE_initDState(ref FSE_DState_t DStatePtr, ref BIT_DStream_t bitD, uint* dt)
        {
            void* ptr = dt;
            FSE_DTableHeader* DTableH = (FSE_DTableHeader*)ptr;
            DStatePtr.state = BIT_readBits(bitD.bitContainer, ref bitD.bitsConsumed, DTableH->tableLog);
            BIT_reloadDStream(ref bitD.bitContainer, ref bitD.bitsConsumed, ref bitD.ptr, bitD.start, bitD.limitPtr);
            DStatePtr.table = dt + 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
private static byte FSE_decodeSymbol(ref FSE_DState_t DStatePtr, nuint bitD_bitContainer, ref uint bitD_bitsConsumed)
        {
            FSE_decode_t DInfo = ((FSE_decode_t*)DStatePtr.table)[DStatePtr.state];
            uint nbBits = DInfo.nbBits;
            byte symbol = DInfo.symbol;
            nuint lowBits = BIT_readBits(bitD_bitContainer, ref bitD_bitsConsumed, nbBits);
            DStatePtr.state = DInfo.newState + lowBits;
            return symbol;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte FSE_decodeSymbolFast(ref FSE_DState_t DStatePtr, nuint bitD_bitContainer, ref uint bitD_bitsConsumed)
        {
            FSE_decode_t DInfo = ((FSE_decode_t*)DStatePtr.table)[DStatePtr.state];
            uint nbBits = DInfo.nbBits;
            byte symbol = DInfo.symbol;
            nuint lowBits = BIT_readBitsFast(bitD_bitContainer, ref bitD_bitsConsumed, nbBits);
            DStatePtr.state = DInfo.newState + lowBits;
            return symbol;
        }
    }
}