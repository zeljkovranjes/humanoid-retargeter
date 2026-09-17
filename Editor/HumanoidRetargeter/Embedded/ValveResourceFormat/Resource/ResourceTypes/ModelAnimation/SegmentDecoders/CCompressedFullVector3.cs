#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeterVrf.Utils;
using System.Runtime.InteropServices;

namespace HumanoidRetargeterVrf.ResourceTypes.ModelAnimation.SegmentDecoders
{
    /// <summary>
    /// Decodes full-precision global::System.Numerics.Vector3 animation data.
    /// </summary>
    public class CCompressedFullVector3 : AnimationSegmentDecoder
    {
        /// <inheritdoc/>
        /// <remarks>
        /// Reads full-precision global::System.Numerics.Vector3 data directly from the data buffer.
        /// </remarks>
        public override void Read(int frameIndex, Frame outFrame)
        {
            var offset = frameIndex * ElementCount;
            var vectorData = MemoryMarshal.Cast<byte, global::System.Numerics.Vector3>(Data);

            for (var i = 0; i < RemapTable.Length; i++)
            {
                outFrame.SetAttribute(RemapTable[i], ChannelAttribute, vectorData[offset + WantedElements[i]]);
            }
        }
    }
}
