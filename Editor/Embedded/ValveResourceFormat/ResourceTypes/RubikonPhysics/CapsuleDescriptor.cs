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
    /// Descriptor for capsule shapes.
    /// </summary>
    public class CapsuleDescriptor : ShapeDescriptor<Shapes.Capsule>
    {
        /// <inheritdoc/>
        public override Shapes.Capsule DeserializeShape(KVObject data) => new(data);
    }
