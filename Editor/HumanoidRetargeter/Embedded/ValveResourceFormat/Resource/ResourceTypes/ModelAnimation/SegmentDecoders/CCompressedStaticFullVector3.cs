#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeterVrf.Utils;
using System.Runtime.InteropServices;

namespace HumanoidRetargeterVrf.ResourceTypes.ModelAnimation.SegmentDecoders
{
    /// <summary>
    /// Decodes static full-precision global::System.Numerics.Vector3 data that doesn't change per frame.
    /// </summary>
    public class CCompressedStaticFullVector3 : AnimationSegmentDecoder
    {
        /// <inheritdoc/>
        /// <remarks>
        /// Reads static global::System.Numerics.Vector3 values that remain constant across all frames.
        /// </remarks>
        public override void Read(int frameIndex, Frame outFrame)
        {
            var vectorData = MemoryMarshal.Cast<byte, global::System.Numerics.Vector3>(Data);

            for (var i = 0; i < RemapTable.Length; i++)
            {
                outFrame.SetAttribute(RemapTable[i], ChannelAttribute, vectorData[WantedElements[i]]);
            }
        }
    }
}
