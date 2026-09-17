#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System;
using HumanoidRetargeterZstd.Unsafe;

namespace HumanoidRetargeterZstd
{
    public class ZstdException : Exception
    {
        public ZstdException(ZSTD_ErrorCode code, string message) : base(message)
            => Code = code;

        public ZSTD_ErrorCode Code { get; }
    }
}
