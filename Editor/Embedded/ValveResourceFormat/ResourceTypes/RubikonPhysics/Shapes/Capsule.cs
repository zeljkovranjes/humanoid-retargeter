#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.Utils;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.Serialization.KeyValues;

namespace HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes;

    /// <summary>
    /// Represents a capsule shape.
    /// </summary>
    public struct Capsule
    {
        /// <summary>
        /// Gets or sets the center points of the capsule.
        /// </summary>
        public global::System.Numerics.Vector3[] Center { get; set; }
        /// <summary>
        /// Gets or sets the radius of the capsule.
        /// </summary>
        public float Radius { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Capsule"/> struct.
        /// </summary>
        public Capsule(KVObject data)
        {
            Center = data.GetArray("m_vCenter").Select(v => v.ToVector3()).ToArray();
            Radius = data.GetFloatProperty("m_flRadius");
        }
    }
