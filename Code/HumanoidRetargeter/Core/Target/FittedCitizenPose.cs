#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using HumanoidRetargeter.Core.Maths;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;
using NVector3 = System.Numerics.Vector3;

namespace HumanoidRetargeter.Core.Target;

/// <summary>Transfers homologous Citizen channels between compiled bind poses, in the same coordinate system.</summary>
public sealed class FittedCitizenPose
{
    private readonly SkeletonModel source;
    private readonly SkeletonModel target;
    private readonly int[] indices;
    private readonly Quaternion[] parentBasis;
    // Each bone's bind rotation, swung so the bone points where the stock bone points at rest.
    private readonly Quaternion[] aimedBind;
    private readonly float[] translationScale;
    // Pinky joints driven by the matching ring joint: source ring index and the rest-frame
    // change from the ring's parent to the pinky's parent (-1 when the pinky keeps its own channel).
    private readonly int[] ringSource;
    private readonly Quaternion[] ringToPinkyParent;
    // Two-handed hold goals: (goal, weapon socket on the anchor hand, support hand, support hand's socket).
    private readonly List<(int Goal, int Anchor, int Hand, int Grip)> supportGoals = new();

    /// <param name="copyRingToPinky">The pinkies follow the ring fingers, as the stock Citizen's CopyPinky
    /// constraints do. Stock clips carry no pinky motion, and fitted rigs disable CopyPinky.</param>
    public FittedCitizenPose(SkeletonModel source, SkeletonModel target, bool copyRingToPinky = false)
    {
        var error = CitizenAnimationSetup.HierarchyError(target, source);
        if (error is not null) throw new ArgumentException(error, nameof(target));
        this.source = source;
        this.target = target;
        indices = new int[target.Count];
        parentBasis = new Quaternion[target.Count];
        aimedBind = new Quaternion[target.Count];
        var aimedWorld = new Quaternion[target.Count];
        translationScale = new float[target.Count];
        ringSource = new int[target.Count];
        ringToPinkyParent = new Quaternion[target.Count];
        // Root travel scales by leg length, not the root's arbitrary scene placement.
        float LegLength(SkeletonModel rig)
        {
            var length = 0f;
            foreach (var name in new[] { "leg_lower_L", "ankle_L", "leg_lower_R", "ankle_R" })
            {
                var index = rig.IndexOf(name);
                if (index >= 0) length += rig[index].RestLocal.Pos.Length();
            }
            return length;
        }
        var sourceLeg = LegLength(source);
        var rootScale = sourceLeg > 0.0001f ? LegLength(target) / sourceLeg : 1f;
        for (var i = 0; i < target.Count; i++)
        {
            var s = indices[i] = source.IndexOf(target[i].Name);
            ringSource[i] = -1;
            if (copyRingToPinky && s >= 0 && target[i].Name.StartsWith("finger_pinky_", StringComparison.Ordinal)
                && source.IndexOf("finger_ring_" + target[i].Name["finger_pinky_".Length..]) is var ring && ring >= 0
                && source[ring].ParentIndex >= 0 && source[s].ParentIndex >= 0)
            {
                ringSource[i] = ring;
                ringToPinkyParent[i] = Quaternion.Normalize(Quaternion.Conjugate(source.RestWorld[source[s].ParentIndex].Rot)
                    * source.RestWorld[source[ring].ParentIndex].Rot);
            }
            var tp = target[i].ParentIndex;
            var parentWorld = tp < 0 ? Quaternion.Identity : aimedWorld[tp];
            aimedBind[i] = target[i].RestLocal.Rot;
            if (s >= 0)
            {
                var sp = source[s].ParentIndex;
                parentBasis[i] = sp < 0 ? Quaternion.Identity
                    : Quaternion.Normalize(Quaternion.Conjugate(parentWorld) * source.RestWorld[sp].Rot);
                // A fitted armature may be bound in another pose (a Blender T-pose against the
                // Citizen's A-pose). Mapping rest to rest kept that difference in every frame:
                // the arms rode ~50 degrees high through all stock animations. Swing each
                // bone onto the stock rest direction first, keeping its own roll.
                if (sp >= 0 && Aim(target, i) is { } fittedChild && source.IndexOf(target[fittedChild].Name) is var stockChild && stockChild >= 0
                    && source[stockChild].ParentIndex == s)
                {
                    var fitted = NVector3.Transform(target[fittedChild].RestLocal.Pos, target[i].RestLocal.Rot);
                    var stock = NVector3.Transform(NVector3.Transform(source[stockChild].RestLocal.Pos, source[s].RestLocal.Rot), parentBasis[i]);
                    aimedBind[i] = Quaternion.Normalize(Swing(fitted, stock) * target[i].RestLocal.Rot);
                }
            }
            aimedWorld[i] = Quaternion.Normalize(parentWorld * aimedBind[i]);
            if (s < 0) continue;
            var sp2 = source[s].ParentIndex;
            var length = source[s].RestLocal.Pos.Length();
            translationScale[i] = sp2 < 0 || length < 0.0001f ? rootScale : target[i].RestLocal.Pos.Length() / length;
        }
        // The Citizen graph pins the support hand to these goals while an item is held. Weapons
        // are never scaled with the character, so the grip separation must not be either.
        foreach (var (goal, anchor, hand, grip) in new[] { ("hand_L_to_R_ikrule", "hold_R", "hand_L", "hold_L"),
            ("hand_R_to_L_ikrule", "hold_L", "hand_R", "hold_R") })
        {
            var names = new[] { goal, anchor, hand, grip };
            if (names.Any(n => source.IndexOf(n) < 0 || target.IndexOf(n) < 0) || target[target.IndexOf(goal)].ParentIndex < 0) continue;
            supportGoals.Add((target.IndexOf(goal), target.IndexOf(anchor), target.IndexOf(hand), target.IndexOf(grip)));
        }
    }

    /// <summary>The child a bone points at: its farthest child, unless another long child points
    /// elsewhere (fingers off a hand, clavicles beside the neck). Twist helpers lie along the bone.</summary>
    private static int? Aim(SkeletonModel rig, int bone)
    {
        int? best = null;
        foreach (var child in rig.Bones)
            if (child.ParentIndex == bone && (best is null || child.RestLocal.Pos.Length() > rig[best.Value].RestLocal.Pos.Length()))
                best = child.Index;
        if (best is null) return null;
        var length = rig[best.Value].RestLocal.Pos.Length();
        if (length < 0.0001f) return null;
        var direction = rig[best.Value].RestLocal.Pos / length;
        foreach (var child in rig.Bones)
        {
            if (child.ParentIndex != bone || child.Index == best) continue;
            var other = child.RestLocal.Pos.Length();
            if (other > length * 0.6f && NVector3.Dot(child.RestLocal.Pos / other, direction) < 0.94f)
                return null;
        }
        return best;
    }

    /// <summary>Shortest rotation taking direction <paramref name="from"/> onto <paramref name="to"/>.</summary>
    private static Quaternion Swing(NVector3 from, NVector3 to)
    {
        if (from.LengthSquared() < 1e-12f || to.LengthSquared() < 1e-12f) return Quaternion.Identity;
        from = NVector3.Normalize(from);
        to = NVector3.Normalize(to);
        var dot = NVector3.Dot(from, to);
        if (dot > 0.999999f) return Quaternion.Identity;
        if (dot < -0.999999f)
        {
            var axis = NVector3.Cross(from, NVector3.UnitX);
            if (axis.LengthSquared() < 1e-6f) axis = NVector3.Cross(from, NVector3.UnitY);
            return Quaternion.CreateFromAxisAngle(NVector3.Normalize(axis), MathF.PI);
        }
        var cross = NVector3.Cross(from, to);
        return Quaternion.Normalize(new Quaternion(cross.X, cross.Y, cross.Z, 1f + dot));
    }

    /// <summary>Stock rest maps to the fitted rest swung onto the stock bone directions; rotation
    /// deltas retain their model-space axes, and local lengths stay fitted.</summary>
    public XForm[] Transfer(XForm[] frame)
    {
        if (frame.Length != source.Count) throw new ArgumentException("Source frame has the wrong bone count.", nameof(frame));
        var output = new XForm[target.Count];
        for (var i = 0; i < target.Count; i++)
        {
            var s = indices[i];
            var bind = target[i].RestLocal;
            if (s < 0) { output[i] = bind; continue; }
            var original = source[s].RestLocal;
            var basis = parentBasis[i];
            var delta = Quaternion.Normalize(frame[s].Rot * Quaternion.Conjugate(original.Rot));
            if (ringSource[i] is var ring and >= 0)
            {
                var ringDelta = Quaternion.Normalize(frame[ring].Rot * Quaternion.Conjugate(source[ring].RestLocal.Rot));
                delta = Quaternion.Normalize(ringToPinkyParent[i] * ringDelta * Quaternion.Conjugate(ringToPinkyParent[i]));
            }
            var rotation = Quaternion.Normalize(basis * delta * Quaternion.Conjugate(basis) * aimedBind[i]);
            var position = bind.Pos + NVector3.Transform(frame[s].Pos - original.Pos, basis) * translationScale[i];
            output[i] = new XForm(position, rotation);
        }
        if (supportGoals.Count == 0) return output;
        var sourceWorld = World(source, frame);
        var targetWorld = World(target, output);
        foreach (var (goal, anchor, hand, grip) in supportGoals)
        {
            // Where the source's support grip sits on the weapon when its wrist is on the goal.
            var sourceGrip = XForm.Compose(sourceWorld[indices[goal]],
                XForm.ToLocal(sourceWorld[indices[hand]], sourceWorld[indices[grip]])).Pos;
            var onWeapon = sourceWorld[indices[anchor]].Inverse().TransformPoint(sourceGrip);
            var rotation = targetWorld[goal].Rot;
            var reach = XForm.ToLocal(targetWorld[hand], targetWorld[grip]).Pos;
            var place = new XForm(targetWorld[anchor].TransformPoint(onWeapon) - NVector3.Transform(reach, rotation), rotation);
            output[goal] = XForm.ToLocal(targetWorld[target[goal].ParentIndex], place);
        }
        return output;
    }

    private static XForm[] World(SkeletonModel skeleton, XForm[] pose)
    {
        var world = new XForm[skeleton.Count];
        foreach (var bone in skeleton.Bones)
            world[bone.Index] = bone.ParentIndex < 0 ? pose[bone.Index] : XForm.Compose(world[bone.ParentIndex], pose[bone.Index]);
        return world;
    }
}
