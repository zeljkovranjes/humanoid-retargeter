#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace HumanoidRetargeter.EditorTools.Embedded.ZstdSharp.Unsafe;

    /* *************************/
    /* double-symbols decoding */
    /* *************************/
    public struct HUF_DEltX2
    {
        /* double-symbols decoding */
        public ushort sequence;
        public byte nbBits;
        public byte length;
    }
