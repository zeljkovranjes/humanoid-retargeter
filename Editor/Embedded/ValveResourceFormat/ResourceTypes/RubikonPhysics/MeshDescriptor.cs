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
    /// Descriptor for mesh shapes.
    /// </summary>
    public class MeshDescriptor : ShapeDescriptor<Shapes.Mesh>
    {
        /// <inheritdoc/>
        public override Shapes.Mesh DeserializeShape(KVObject data) => new(data);
    }
