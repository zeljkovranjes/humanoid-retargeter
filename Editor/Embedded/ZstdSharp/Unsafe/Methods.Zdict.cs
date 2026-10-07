#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using static HumanoidRetargeter.EditorTools.Embedded.ZstdSharp.UnsafeHelper;

namespace HumanoidRetargeter.EditorTools.Embedded.ZstdSharp.Unsafe;

    public static unsafe partial class Methods
    {
        /*-********************************************************
         *  Helper functions
         **********************************************************/
        public static bool ZDICT_isError(nuint errorCode)
        {
            return ERR_isError(errorCode);
        }

        public static string ZDICT_getErrorName(nuint errorCode)
        {
            return ERR_getErrorName(errorCode);
        }
    }
