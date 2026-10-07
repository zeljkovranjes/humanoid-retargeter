#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace HumanoidRetargeter.EditorTools.Embedded.ZstdSharp.Unsafe;

    public struct FSE_decode_t
    {
        public ushort newState;
        public byte symbol;
        public byte nbBits;
    }
