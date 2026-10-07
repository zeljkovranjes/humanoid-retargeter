#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace HumanoidRetargeter.EditorTools.Embedded.ZstdSharp.Unsafe;

    public struct blockProperties_t
    {
        public blockType_e blockType;
        public uint lastBlock;
        public uint origSize;
    }
