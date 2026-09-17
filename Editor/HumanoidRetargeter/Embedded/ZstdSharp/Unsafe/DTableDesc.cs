#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace HumanoidRetargeterZstd.Unsafe
{
    /*-***************************/
    /*  generic DTableDesc       */
    /*-***************************/
    public struct DTableDesc
    {
        public byte maxTableLog;
        public byte tableType;
        public byte tableLog;
        public byte reserved;
    }
}