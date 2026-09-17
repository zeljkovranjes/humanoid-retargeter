#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using static HumanoidRetargeterZstd.UnsafeHelper;

namespace HumanoidRetargeterZstd.Unsafe
{
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
}