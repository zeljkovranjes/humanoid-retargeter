#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeterVrf.Utils;
using HumanoidRetargeterVrf.Serialization.KeyValues;

namespace HumanoidRetargeterVrf.ResourceTypes.RubikonPhysics.Shapes
{
    /// <summary>
    /// Represents a sphere shape.
    /// </summary>
    public struct Sphere
    {
        /// <summary>
        /// Gets or sets the center of the sphere.
        /// </summary>
        public global::System.Numerics.Vector3 Center { get; set; }
        /// <summary>
        /// Gets or sets the radius of the sphere.
        /// </summary>
        public float Radius { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Sphere"/> struct.
        /// </summary>
        public Sphere(KVObject data)
        {
            Center = data.GetSubCollection("m_vCenter").ToVector3();
            Radius = data.GetFloatProperty("m_flRadius");
        }
    }
}
