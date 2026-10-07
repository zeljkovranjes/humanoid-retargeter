#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.Utils;
using System.IO;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.Serialization.KeyValues;

namespace HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.ResourceTypes.RubikonPhysics;

    /// <summary>
    /// Base descriptor for physics shapes.
    /// </summary>
    public class ShapeDescriptor<T> where T : struct
    {
        /// <summary>
        /// Gets or sets the collision attribute index.
        /// </summary>
        public int CollisionAttributeIndex { get; set; }
        /// <summary>
        /// Gets or sets the surface property index.
        /// </summary>
        public int SurfacePropertyIndex { get; set; }
        /// <summary>
        /// Gets or sets the user-friendly name.
        /// </summary>
        public string? UserFriendlyName { get; set; }

        /// <summary>
        /// Gets or sets the shape.
        /// </summary>
        public T Shape { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ShapeDescriptor{T}"/> class.
        /// </summary>
        protected ShapeDescriptor()
        {
        }

        /// <summary>
        /// Transfers data from a KVObject.
        /// </summary>
        public void KV3Transfer(KVObject data)
        {
            CollisionAttributeIndex = data.GetInt32Property("m_nCollisionAttributeIndex");
            SurfacePropertyIndex = data.GetInt32Property("m_nSurfacePropertyIndex");
            UserFriendlyName = data.GetStringProperty("m_UserFriendlyName");

            var memberName = typeof(T).Name;
            var shapeData = data.GetSubCollection("m_" + memberName) ?? throw new InvalidDataException("Member name is not correct for shape type: " + memberName);
            Shape = DeserializeShape(shapeData);
        }

        /// <summary>
        /// Deserializes the shape from a KVObject.
        /// </summary>
        public virtual T DeserializeShape(KVObject data)
        {
            throw new NotImplementedException();
        }
    }
