#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace HumanoidRetargeterZstd.Unsafe
{
    public enum ZSTD_dStage
    {
        ZSTDds_getFrameHeaderSize,
        ZSTDds_decodeFrameHeader,
        ZSTDds_decodeBlockHeader,
        ZSTDds_decompressBlock,
        ZSTDds_decompressLastBlock,
        ZSTDds_checkChecksum,
        ZSTDds_decodeSkippableHeader,
        ZSTDds_skipFrame
    }
}