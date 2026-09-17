#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace HumanoidRetargeterZstd.Unsafe
{
    /*-********************************************
     *  bitStream decoding API (read backward)
     **********************************************/
    public unsafe struct BIT_DStream_t
    {
        public nuint bitContainer;
        public uint bitsConsumed;
        public sbyte* ptr;
        public sbyte* start;
        public sbyte* limitPtr;
    }
}