#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.Utils;
using System.Runtime.InteropServices;

namespace HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders;

    /// <summary>
    /// Decodes full-precision quaternion animation data.
    /// </summary>
    public class CCompressedFullQuaternion : AnimationSegmentDecoder
    {
        /// <inheritdoc/>
        /// <remarks>
        /// Reads full-precision quaternion data directly from the data buffer.
        /// </remarks>
        public override void Read(int frameIndex, Frame outFrame)
        {
            var offset = frameIndex * ElementCount;
            var quaternionData = MemoryMarshal.Cast<byte, global::System.Numerics.Quaternion>(Data);

            for (var i = 0; i < RemapTable.Length; i++)
            {
                outFrame.SetAttribute(RemapTable[i], ChannelAttribute, quaternionData[offset + WantedElements[i]]);
            }
        }
    }
