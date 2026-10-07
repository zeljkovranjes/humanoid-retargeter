#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace HumanoidRetargeter.EditorTools.Embedded.ZstdSharp.Unsafe;

    /*!
     * @internal
     * @brief Enum to indicate whether a pointer is aligned.
     */
    public enum XXH_alignment
    {
        /*!< Aligned */
        XXH_aligned,
        /*!< Possibly unaligned */
        XXH_unaligned
    }
