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
    private readonly float[] translationScale;
    // Two-handed hold goals: (goal, weapon socket on the anchor hand, support hand, support hand's socket).
    private readonly List<(int Goal, int Anchor, int Hand, int Grip)> supportGoals = new();

    public FittedCitizenPose(SkeletonModel source, SkeletonModel target)
    {
        var error = CitizenAnimationSetup.HierarchyError(target, source);
        if (error is not null) throw new ArgumentException(error, nameof(target));
        this.source = source;
        this.target = target;
        indices = new int[target.Count];
        parentBasis = new Quaternion[target.Count];
        translationScale = new float[target.Count];
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
            if (s < 0) continue;
            var sp = source[s].ParentIndex;
            var tp = target[i].ParentIndex;
            parentBasis[i] = sp < 0 ? Quaternion.Identity
                : Quaternion.Normalize(Quaternion.Conjugate(target.RestWorld[tp].Rot) * source.RestWorld[sp].Rot);
            var length = source[s].RestLocal.Pos.Length();
            translationScale[i] = sp < 0 || length < 0.0001f ? rootScale : target[i].RestLocal.Pos.Length() / length;
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

    /// <summary>Rest maps to rest; rotation deltas retain their model-space axes, and local lengths stay fitted.</summary>
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
            var rotation = Quaternion.Normalize(basis * delta * Quaternion.Conjugate(basis) * bind.Rot);
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
