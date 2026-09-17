#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace HumanoidRetargeterZstd.Unsafe
{
    public enum ZSTD_litLocation_e
    {
        /* Stored entirely within litExtraBuffer */
        ZSTD_not_in_dst = 0,
        /* Stored entirely within dst (in memory after current output write) */
        ZSTD_in_dst = 1,
        /* Split between litExtraBuffer and dst */
        ZSTD_split = 2
    }
}