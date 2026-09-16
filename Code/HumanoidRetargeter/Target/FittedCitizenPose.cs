#nullable enable annotations
using System;
using System.Numerics;
using HumanoidRetargeter.Maths;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;
using NVector3 = System.Numerics.Vector3;

namespace HumanoidRetargeter.Target;

/// <summary>Transfers homologous Citizen channels between compiled bind poses, in the same coordinate system.</summary>
public sealed class FittedCitizenPose
{
    private readonly SkeletonModel source;
    private readonly SkeletonModel target;
    private readonly int[] indices;
    private readonly Quaternion[] parentBasis;
    private readonly float[] translationScale;

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
        return output;
    }
}
