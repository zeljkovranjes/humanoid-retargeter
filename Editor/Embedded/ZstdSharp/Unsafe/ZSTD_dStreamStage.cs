#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace HumanoidRetargeter.EditorTools.Embedded.ZstdSharp.Unsafe;

    public enum ZSTD_dStreamStage
    {
        zdss_init = 0,
        zdss_loadHeader,
        zdss_read,
        zdss_load,
        zdss_flush
    }
