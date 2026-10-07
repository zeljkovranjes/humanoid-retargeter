#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace HumanoidRetargeter.EditorTools.Embedded.ZstdSharp.Unsafe;

    public enum ZSTD_refMultipleDDicts_e
    {
        /* Note: this enum controls ZSTD_d_refMultipleDDicts */
        ZSTD_rmd_refSingleDDict = 0,
        ZSTD_rmd_refMultipleDDicts = 1
    }
