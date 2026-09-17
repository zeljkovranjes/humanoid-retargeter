#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace HumanoidRetargeterZstd.Unsafe
{
    public enum ZSTD_dStreamStage
    {
        zdss_init = 0,
        zdss_loadHeader,
        zdss_read,
        zdss_load,
        zdss_flush
    }
}