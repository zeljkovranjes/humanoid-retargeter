#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace HumanoidRetargeterZstd.Unsafe
{
    public struct seq_t
    {
        public nuint litLength;
        public nuint matchLength;
        public nuint offset;
    }
}