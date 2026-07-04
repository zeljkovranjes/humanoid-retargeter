using System;
using System.Collections.Generic;
using System.Numerics;
using HumanoidRetargeter.Mapping;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Target;

namespace HumanoidRetargeter.Cleanup;

using Vector3 = System.Numerics.Vector3; // s&box compat: shadow engine's global-namespace Vector3 (see Code/HumanoidRetargeter/Assembly.cs)

/// <summary>
/// Drives unmapped limb TWIST bones from the roll of the joint they distribute.
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
/// </remarks>
public static class TwistBoneFollow
{
    private static readonly (BoneRole Parent, BoneRole Child)[] Segments =
    {
        (BoneRole.UpperArmL, BoneRole.LowerArmL), (BoneRole.LowerArmL, BoneRole.HandL),
        (BoneRole.UpperArmR, BoneRole.LowerArmR), (BoneRole.LowerArmR, BoneRole.HandR),
        (BoneRole.UpperLegL, BoneRole.LowerLegL), (BoneRole.LowerLegL, BoneRole.FootL),
        (BoneRole.UpperLegR, BoneRole.LowerLegR), (BoneRole.LowerLegR, BoneRole.FootR),
    };

    /// <summary>Applies the pass in place; returns how many twist bones were driven.</summary>
    public static int Apply(
        IReadOnlyList<XForm[]> frames, TargetRig rig, IReadOnlySet<int> excluded)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(rig);
        var skeleton = rig.Skeleton;

        var twists = new List<(int Bone, int Driver, Vector3 Axis, float Fraction)>();
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
                var along = Vector3.Dot(pos, axis);
                var fraction = along / length;
                if (fraction is < 0.05f or > 1.1f)
                    continue;
                var offAxis = (pos - axis * along).Length();
                if (offAxis > MathF.Tan(15f * MathF.PI / 180f) * MathF.Max(along, 1e-3f))
                    continue;
                twists.Add((i, child, axis, Math.Clamp(fraction, 0f, 1f)));
            }
        }
        if (twists.Count == 0)
            return 0;

        foreach (var frame in frames)
        {
            foreach (var (bone, driver, axis, fraction) in twists)
            {
                // The driver's rotation delta from rest, in the shared parent's space.
                var delta = MathQ.Normalize(
                    frame[driver].Rot * Quaternion.Conjugate(skeleton[driver].RestLocal.Rot));
                // Twist component about the limb axis (swing-twist decomposition).
                var proj = Vector3.Dot(new Vector3(delta.X, delta.Y, delta.Z), axis);
                var twist = new Quaternion(axis * proj, delta.W);
                if (twist.LengthSquared() < 1e-12f)
                    continue; // 180-degree pure swing: twist undefined, keep rest
                twist = Quaternion.Normalize(twist);
                // Scale the roll by the twist bone's position along the segment.
                var angle = 2f * MathF.Atan2(proj >= 0 ? new Vector3(twist.X, twist.Y, twist.Z).Length()
                    : -new Vector3(twist.X, twist.Y, twist.Z).Length(), twist.W);
                var scaled = Quaternion.CreateFromAxisAngle(axis, angle * fraction);
                frame[bone] = new XForm(
                    frame[bone].Pos, MathQ.Normalize(scaled * skeleton[bone].RestLocal.Rot));
            }
        }
        return twists.Count;
    }
}
