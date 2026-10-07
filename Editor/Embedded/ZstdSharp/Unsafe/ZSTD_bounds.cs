#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace HumanoidRetargeter.EditorTools.Embedded.ZstdSharp.Unsafe;

    public struct ZSTD_bounds
    {
        public nuint error;
        public int lowerBound;
        public int upperBound;
    }
