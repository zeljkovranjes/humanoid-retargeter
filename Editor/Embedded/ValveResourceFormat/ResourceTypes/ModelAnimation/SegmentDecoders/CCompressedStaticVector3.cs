#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.Utils;
using System.Runtime.InteropServices;

namespace HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders;

    /// <summary>
    /// Decodes static global::System.Numerics.Vector3 data using half-precision floats that doesn't change per frame.
    /// </summary>
    public class CCompressedStaticVector3 : AnimationSegmentDecoder
    {
        /// <inheritdoc/>
        /// <remarks>
        /// Reads static half-precision global::System.Numerics.Vector3 values that remain constant across all frames.
        /// </remarks>
        public override void Read(int frameIndex, Frame outFrame)
        {
            var halfVectorData = MemoryMarshal.Cast<byte, Half3>(Data);

            for (var i = 0; i < RemapTable.Length; i++)
            {
                outFrame.SetAttribute(RemapTable[i], ChannelAttribute, halfVectorData[WantedElements[i]]);
            }
        }
    }
