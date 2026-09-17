#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeterVrf.Utils;
#nullable disable

using System.Diagnostics;

namespace HumanoidRetargeterVrf.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents a bone in a model skeleton.
    /// </summary>
    [DebuggerDisplay("{Name} (Index: {Index})")]
    public class Bone
    {
        /// <summary>
        /// Gets the index of the bone in the skeleton.
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Gets the bone flags.
        /// </summary>
        public ModelSkeletonBoneFlags Flags { get; }

        /// <summary>
        /// Gets the parent bone, or null if this is a root bone.
        /// </summary>
        public Bone Parent { get; private set; }

        /// <summary>
        /// Gets the list of child bones.
        /// </summary>
        public List<Bone> Children { get; } = [];

        /// <summary>
        /// Gets the name of the bone.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the bone's position in parent space.
        /// </summary>
        public global::System.Numerics.Vector3 Position { get; }

        /// <summary>
        /// Gets the bone's rotation in parent space.
        /// </summary>
        public global::System.Numerics.Quaternion Angle { get; }

        /// <summary>
        /// Gets the bind pose transformation matrix.
        /// </summary>
        public global::System.Numerics.Matrix4x4 BindPose { get; }

        /// <summary>
        /// Gets the inverse bind pose transformation matrix.
        /// </summary>
        public global::System.Numerics.Matrix4x4 InverseBindPose { get; }

        /// <summary>
        /// Gets a value indicating whether this bone is part of procedural cloth simulation.
        /// </summary>
        public bool IsProceduralCloth => (Flags & ModelSkeletonBoneFlags.ProceduralCloth) == ModelSkeletonBoneFlags.ProceduralCloth;

        /// <summary>
        /// Initializes a new instance of the <see cref="Bone"/> class.
        /// </summary>
        public Bone(int index, string name, global::System.Numerics.Vector3 position, global::System.Numerics.Quaternion rotation, ModelSkeletonBoneFlags flags)
        {
            Index = index;
            Name = name;
            Flags = flags;

            Position = position;
            Angle = rotation;

            // Calculate matrices
            BindPose = global::System.Numerics.Matrix4x4.CreateFromQuaternion(rotation) * global::System.Numerics.Matrix4x4.CreateTranslation(position);

            if (!global::System.Numerics.Matrix4x4.Invert(BindPose, out var inverseBindPose))
            {
                throw new InvalidOperationException("Matrix invert failed");
            }

            InverseBindPose = inverseBindPose;
        }

        /// <summary>
        /// Sets the parent bone for this bone.
        /// </summary>
        public void SetParent(Bone parent)
        {
            if (!Children.Contains(parent))
            {
                Parent = parent;
                parent.Children.Add(this);
            }
        }
    }
}
