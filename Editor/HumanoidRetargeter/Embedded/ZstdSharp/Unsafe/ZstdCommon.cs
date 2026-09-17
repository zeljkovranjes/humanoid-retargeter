#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace HumanoidRetargeterZstd.Unsafe
{
    public static unsafe partial class Methods
    {

        /*! ZSTD_isError() :
         *  tells if a return value is an error code
         *  symbol is required for external callers */
        public static bool ZSTD_isError(nuint code)
        {
            return ERR_isError(code);
        }

        /*! ZSTD_getErrorName() :
         *  provides error code string from function result (useful for debugging) */
        public static string ZSTD_getErrorName(nuint code)
        {
            return ERR_getErrorName(code);
        }

        /*! ZSTD_getError() :
         *  convert a `size_t` function result into a proper ZSTD_errorCode enum */
        public static ZSTD_ErrorCode ZSTD_getErrorCode(nuint code)
        {
            return ERR_getErrorCode(code);
        }
    }
}