#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Numerics;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Target;

namespace HumanoidRetargeter.Core.Cleanup;

using Vector3 = System.Numerics.Vector3; // s&box compat: shadow engine's global-namespace Vector3 (see Code/HumanoidRetargeter/Assembly.cs)

/// <summary>
/// Drives unmapped limb deform bones from the joints whose motion they distribute.
/// Auto-rigged exports (Auto-Rig Pro <c>forearm_twist.l</c>, AdvancedSkeleton
/// <c>ElbowPart1_L</c>, Biped <c>Bip01 L ForeTwist</c>) spread limb roll across helper
/// bones the game constrains at runtime; a baked retarget that leaves them at rest
/// candy-wraps the skin — the reported wrist "spike fans" when the hand pronates.
/// </summary>
/// <remarks>
/// Detection is geometric, name-free: an UNMAPPED bone whose parent is a mapped limb
/// bone (upper/lower arm or leg) and whose rest position lies ON the segment from that
/// parent to the parent's mapped chain child (within 15° of the axis, fraction
/// 0.05..1.1 along it). Each detected twist follows the chain child's per-frame local
/// ROLL — the twist component of its rotation delta about the limb axis — scaled by the
/// twist's fractional position (a bone at 60% of the forearm takes 60% of the hand's
/// roll; ARP's proximal <c>arm_twist</c> at fraction ~0 correctly takes ~none). Pure
/// swing carries no twist component, so elbows/knees bending never move these bones.
/// Serial deform bones between two mapped limb joints are handled separately: their
/// world-space motion delta is interpolated between the endpoints while both mapped
/// endpoint transforms remain unchanged. This covers rigs that split each bend/twist
/// section into two weighted bones without relying on exporter-specific names. An
/// unmapped sibling at the mapped joint's same pivot follows its complete rotation;
/// this covers dual control/deform rigs where a mechanism forearm/femur drives the next
/// joint while coincident anatomical bones carry the skin.
/// </remarks>
public static class TwistBoneFollow
{
    private static readonly (BoneRole Parent, BoneRole Child)[] Segments =
    {
        (BoneRole.ClavicleL, BoneRole.UpperArmL),
        (BoneRole.UpperArmL, BoneRole.LowerArmL), (BoneRole.LowerArmL, BoneRole.HandL),
        (BoneRole.ClavicleR, BoneRole.UpperArmR),
        (BoneRole.UpperArmR, BoneRole.LowerArmR), (BoneRole.LowerArmR, BoneRole.HandR),
        (BoneRole.Hips, BoneRole.UpperLegL),
        (BoneRole.UpperLegL, BoneRole.LowerLegL), (BoneRole.LowerLegL, BoneRole.FootL),
        (BoneRole.Hips, BoneRole.UpperLegR),
        (BoneRole.UpperLegR, BoneRole.LowerLegR), (BoneRole.LowerLegR, BoneRole.FootR),
    };

    internal readonly record struct InlineBone(int Bone, int Parent, int Child, float Fraction);

    internal readonly record struct FullFollower(int Bone, int Driver);

    internal readonly record struct TwistBone(int Bone, int Driver, Vector3 Axis, float Fraction);

    internal readonly record struct InlineGroup(int Parent, int Child, InlineBone[] Entries);

    /// <summary>Applies the pass in place; returns how many limb helpers were driven.</summary>
    public static int Apply(
        IReadOnlyList<XForm[]> frames, TargetRig rig, IReadOnlySet<int>? excluded)
    {
        ArgumentNullException.ThrowIfNull(frames);
        var follow = Prepare(rig, excluded);
        if (follow is null)
            return 0;

        foreach (var frame in frames)
            follow.Apply(frame);
        return follow.Count;
    }

    /// <summary>
    /// Finds <paramref name="rig"/>'s limb helpers once, to drive them in any number of poses
    /// with <see cref="Follow.Apply"/>; null when it has none. Bones in
    /// <paramref name="excluded"/> are left alone.
    /// </summary>
    public static Follow? Prepare(TargetRig rig, IReadOnlySet<int>? excluded)
    {
        ArgumentNullException.ThrowIfNull(rig);
        var skeleton = rig.Skeleton;

        var twists = new List<TwistBone>();
        var fullFollowers = new List<FullFollower>();
        var fullFollowerBones = new HashSet<int>();
        foreach (var (parentRole, childRole) in Segments)
        {
            if (rig.BoneForRole(parentRole) is not { } parent
                || rig.BoneForRole(childRole) is not { } child
                || skeleton[child].ParentIndex != parent)
                continue;

            // Limb axis and length in the PARENT's local space (the chain child's rest
            // local translation).
            var axis = skeleton[child].RestLocal.Pos;
            var length = axis.Length();
            if (length < 1e-3f)
                continue;
            axis /= length;

            for (var i = 0; i < skeleton.Count; i++)
            {
                if (i == child || skeleton[i].ParentIndex != parent
                    || rig.RoleOf(i) is not null || excluded?.Contains(i) == true)
                    continue;
                var pos = skeleton[i].RestLocal.Pos;
                // Blender control/deform exports commonly put a mechanism joint and one
                // or more skinned anatomical joints at the same pivot (MCH_forearm beside
                // radius/ulna, MCH_femur beside femur). The mapped mechanism drives the
                // next joint, but its deform siblings need the complete bend and roll;
                // treating them as ordinary twist bones copies roll only and leaves the
                // mesh behind while the hand/leg moves away.
                if ((pos - skeleton[child].RestLocal.Pos).Length()
                    <= MathF.Max(0.01f, length * 0.01f))
                {
                    if (fullFollowerBones.Add(i))
                        fullFollowers.Add(new FullFollower(i, child));
                    continue;
                }
                var along = Vector3.Dot(pos, axis);
                var fraction = along / length;
                if (fraction is < 0.05f or > 1.1f)
                    continue;
                var offAxis = (pos - axis * along).Length();
                if (offAxis > MathF.Tan(15f * MathF.PI / 180f) * MathF.Max(along, 1e-3f))
                    continue;
                twists.Add(new TwistBone(i, child, axis, Math.Clamp(fraction, 0f, 1f)));
            }
        }
        var inline = FindInlineBones(rig, excluded);
        if (twists.Count == 0 && inline.Count == 0 && fullFollowers.Count == 0)
            return null;

        return new Follow(skeleton, twists.ToArray(), fullFollowers.ToArray(), inline);
    }

    /// <summary>
    /// A rig's limb helpers, found once by <see cref="Prepare"/>. Driving them in a pose
    /// allocates nothing, so a live character can run it every frame. Not thread-safe: it
    /// owns the buffers the inline bones are solved in.
    /// </summary>
    public sealed class Follow
    {
        private readonly Skeleton.Skeleton _skeleton;
        private readonly TwistBone[] _twists;
        private readonly FullFollower[] _fullFollowers;
        private readonly InlineGroup[] _inlineGroups;
        private readonly int[] _inlineWritten;
        private readonly XForm[] _world;
        private readonly XForm[] _desired;

        /// <summary>How many limb helpers each pose has driven.</summary>
        public int Count { get; }

        internal Follow(Skeleton.Skeleton skeleton, TwistBone[] twists,
            FullFollower[] fullFollowers, List<InlineBone> inline)
        {
            _skeleton = skeleton;
            _twists = twists;
            _fullFollowers = fullFollowers;
            _inlineGroups = inline
                .GroupBy(entry => (entry.Parent, entry.Child))
                .Select(group => new InlineGroup(group.Key.Parent, group.Key.Child, group.ToArray()))
                .ToArray();
            // Written parents first, so each local is taken against its parent's final world
            _inlineWritten = inline.Select(entry => entry.Bone)
                .Concat(_inlineGroups.Select(group => group.Child))
                .Distinct()
                .OrderBy(index => index)
                .ToArray();
            _world = inline.Count > 0 ? new XForm[skeleton.Count] : Array.Empty<XForm>();
            _desired = inline.Count > 0 ? new XForm[skeleton.Count] : Array.Empty<XForm>();
            Count = twists.Length + inline.Count + fullFollowers.Length;
        }

        /// <summary>Drives the helpers in <paramref name="frame"/>, a parent-local pose, in place.</summary>
        public void Apply(Span<XForm> frame)
        {
            if (frame.Length < _skeleton.Count)
                throw new ArgumentException(
                    $"Pose has {frame.Length} bones but the skeleton has {_skeleton.Count}.", nameof(frame));

            foreach (var follower in _fullFollowers)
            {
                // Both bones share a parent, so the driver's local-space rotation delta
                // can be applied directly while retaining the deform bone's bind offset.
                var delta = MathQ.Normalize(frame[follower.Driver].Rot
                    * Quaternion.Conjugate(_skeleton[follower.Driver].RestLocal.Rot));
                frame[follower.Bone] = new XForm(
                    frame[follower.Bone].Pos,
                    MathQ.Normalize(delta * _skeleton[follower.Bone].RestLocal.Rot));
            }
            foreach (var (bone, driver, axis, fraction) in _twists)
            {
                // The driver's rotation delta from rest, in the shared parent's space,
                // forced to the SHORTEST arc (W >= 0) so the twist angle below is
                // continuous in (-180°, 180°) and never flips representation.
                var delta = MathQ.Normalize(
                    frame[driver].Rot * Quaternion.Conjugate(_skeleton[driver].RestLocal.Rot));
                if (delta.W < 0f)
                    delta = new Quaternion(-delta.X, -delta.Y, -delta.Z, -delta.W);
                // Twist component about the limb axis (swing-twist decomposition).
                var proj = Vector3.Dot(new Vector3(delta.X, delta.Y, delta.Z), axis);
                // Ill-conditioned when the delta approaches a pure 180° SWING (both the
                // axis projection and W collapse toward 0): the decomposition then
                // amplifies noise into huge fake rolls — measured on a throw clip, the
                // kicking foot injected ±99° into the calf twist bone and the calf skin
                // flipped upward ("the leg is up"). Keep rest instead.
                var conditioning = MathF.Sqrt(proj * proj + delta.W * delta.W);
                if (conditioning < 0.2f)
                    continue;
                var angle = 2f * MathF.Atan2(proj, delta.W);
                var scaled = Quaternion.CreateFromAxisAngle(axis, angle * fraction);
                frame[bone] = new XForm(
                    frame[bone].Pos, MathQ.Normalize(scaled * _skeleton[bone].RestLocal.Rot));
            }

            if (_inlineGroups.Length > 0)
                FollowInlineBones(frame);
        }

        private void FollowInlineBones(Span<XForm> frame)
        {
            for (var i = 0; i < _skeleton.Count; i++)
            {
                var parent = _skeleton[i].ParentIndex;
                _world[i] = parent < 0 ? frame[i] : XForm.Compose(_world[parent], frame[i]);
                _desired[i] = _world[i];
            }

            foreach (var group in _inlineGroups)
            {
                var parentDelta = MathQ.Normalize(_world[group.Parent].Rot
                    * Quaternion.Conjugate(_skeleton.RestWorld[group.Parent].Rot));
                var childDelta = MathQ.Normalize(_world[group.Child].Rot
                    * Quaternion.Conjugate(_skeleton.RestWorld[group.Child].Rot));
                if (Quaternion.Dot(parentDelta, childDelta) < 0f)
                    childDelta = new Quaternion(
                        -childDelta.X, -childDelta.Y, -childDelta.Z, -childDelta.W);

                foreach (var entry in group.Entries)
                {
                    var delta = MathQ.Normalize(Quaternion.Slerp(
                        parentDelta, childDelta, entry.Fraction));
                    _desired[entry.Bone] = new XForm(
                        _world[entry.Bone].Pos,
                        MathQ.Normalize(delta * _skeleton.RestWorld[entry.Bone].Rot));
                }
            }

            // The mapped endpoint is written too: its already-solved world transform stays
            // exact after its intermediary parent starts following the motion.
            foreach (var bone in _inlineWritten)
            {
                var parent = _skeleton[bone].ParentIndex;
                frame[bone] = parent < 0
                    ? _desired[bone]
                    : XForm.ToLocal(_desired[parent], _desired[bone]);
            }
        }
    }

    private static List<InlineBone> FindInlineBones(
        TargetRig rig, IReadOnlySet<int>? excluded)
    {
        var skeleton = rig.Skeleton;
        var result = new List<InlineBone>();
        var seen = new HashSet<int>();
        foreach (var (parentRole, childRole) in Segments)
        {
            if (rig.BoneForRole(parentRole) is not { } parent
                || rig.BoneForRole(childRole) is not { } child)
                continue;

            var path = new List<int>();
            for (var bone = skeleton[child].ParentIndex;
                 bone >= 0 && bone != parent;
                 bone = skeleton[bone].ParentIndex)
                path.Add(bone);
            if (path.Count == 0
                || skeleton[path[^1]].ParentIndex != parent
                || path.Any(bone => rig.RoleOf(bone) is not null
                    || excluded?.Contains(bone) == true))
                continue;
            path.Reverse();

            var length = 0f;
            var previous = parent;
            foreach (var bone in path.Append(child))
            {
                length += (skeleton.RestWorld[bone].Pos
                    - skeleton.RestWorld[previous].Pos).Length();
                previous = bone;
            }
            if (length < 1e-3f)
                continue;

            var along = 0f;
            previous = parent;
            foreach (var bone in path)
            {
                along += (skeleton.RestWorld[bone].Pos
                    - skeleton.RestWorld[previous].Pos).Length();
                if (seen.Add(bone))
                    result.Add(new InlineBone(bone, parent, child, along / length));
                previous = bone;
            }
        }
        return result;
    }
}
