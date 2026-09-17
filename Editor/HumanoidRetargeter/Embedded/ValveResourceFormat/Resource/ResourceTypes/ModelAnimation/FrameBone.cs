#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeterVrf.Utils;
namespace HumanoidRetargeterVrf.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents the transform of a bone in a single animation frame.
    /// </summary>
    public struct FrameBone
    {
        /// <summary>
        /// Gets or sets the position of the bone.
        /// </summary>
        public global::System.Numerics.Vector3 Position { get; set; }

        /// <summary>
        /// Gets or sets the rotation of the bone.
        /// </summary>
        public global::System.Numerics.Quaternion Angle { get; set; }

        /// <summary>
        /// Gets or sets the scale of the bone.
        /// </summary>
        public float Scale { get; set; }
    }
}
