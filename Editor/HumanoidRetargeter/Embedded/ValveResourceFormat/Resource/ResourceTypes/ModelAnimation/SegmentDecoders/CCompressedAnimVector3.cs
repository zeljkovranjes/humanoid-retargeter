#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeterVrf.Utils;
using System.Runtime.InteropServices;

namespace HumanoidRetargeterVrf.ResourceTypes.ModelAnimation.SegmentDecoders
{
    /// <summary>
    /// Decodes compressed global::System.Numerics.Vector3 animation data using half-precision floats.
    /// </summary>
    public class CCompressedAnimVector3 : AnimationSegmentDecoder
    {
        /// <inheritdoc/>
        /// <remarks>
        /// Reads half-precision global::System.Numerics.Vector3 data and converts it to full precision for the output frame.
        /// </remarks>
        public override void Read(int frameIndex, Frame outFrame)
        {
            var offset = frameIndex * ElementCount;
            var halfVectorData = MemoryMarshal.Cast<byte, Half3>(Data);

            for (var i = 0; i < RemapTable.Length; i++)
            {
                var elementIndex = WantedElements[i];

                outFrame.SetAttribute(
                    RemapTable[i],
                    ChannelAttribute,
                    halfVectorData[offset + elementIndex]
                );
            }
        }
    }
}
