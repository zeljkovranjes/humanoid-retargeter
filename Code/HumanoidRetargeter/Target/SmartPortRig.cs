#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using HumanoidRetargeter.Mapping;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Solve;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;
using NVector3 = System.Numerics.Vector3;

namespace HumanoidRetargeter.Target;

/// <summary>Fits a source animation rig around an unchanged target skin skeleton.</summary>
public sealed class SmartPortRig
{
    public SkeletonModel Source { get; }
    public SkeletonModel Target { get; }
    public IReadOnlyDictionary<string, string> BoneNames => names;
    public float MotionScale { get; }
    readonly Dictionary<string, string> names = new(StringComparer.Ordinal);
    readonly RuntimePoseRetargeter solver;
    readonly int[] sourceIndices;
    readonly int[] sourceParents;
    readonly HashSet<int> roleBones;
    readonly XForm[] reference;
    readonly int[] limbEnds;

    public SmartPortRig(SkeletonModel source, SkeletonModel target)
    {
        Source = source;
        foreach (var bone in source.Bones.Concat(target.Bones))
            if (!float.IsFinite(bone.RestLocal.Pos.LengthSquared()) || !float.IsFinite(bone.RestLocal.Rot.LengthSquared())
                || bone.RestLocal.Rot.LengthSquared() < .0001f) throw new ArgumentException("Invalid bind transform: " + bone.Name);
        var sourceMap = Map(source);
        var targetMap = Map(target);
        var required = new[] { BoneRole.Hips, BoneRole.Head, BoneRole.UpperArmL, BoneRole.UpperArmR,
            BoneRole.LowerArmL, BoneRole.LowerArmR, BoneRole.HandL, BoneRole.HandR,
            BoneRole.UpperLegL, BoneRole.UpperLegR, BoneRole.LowerLegL, BoneRole.LowerLegR, BoneRole.FootL, BoneRole.FootR };
        foreach (var role in required)
            if (!sourceMap.RoleToBone.ContainsKey(role) || !targetMap.RoleToBone.ContainsKey(role))
                throw new ArgumentException($"Smart Port could not match humanoid role {role} on both models.");
        limbEnds = new[] { BoneRole.LowerArmL, BoneRole.LowerArmR, BoneRole.HandL, BoneRole.HandR,
            BoneRole.LowerLegL, BoneRole.LowerLegR, BoneRole.FootL, BoneRole.FootR }.Select(r => sourceMap.RoleToBone[r]).ToArray();
        var lookup = target.Bones.ToDictionary(b => b.Name, b => b.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var bone in source.Bones)
            if (lookup.TryGetValue(bone.Name, out var match)) names.Add(bone.Name, match);
        foreach (var (role, index) in sourceMap.RoleToBone)
            if (targetMap.RoleToBone.TryGetValue(role, out var other)) names[source[index].Name] = target[other].Name;
        if (names.Values.Distinct(StringComparer.Ordinal).Count() != names.Count)
            throw new ArgumentException("Smart Port bone mapping is ambiguous.");

        float LegLength(SkeletonModel rig, MappingResult map) => new[] { BoneRole.LowerLegL, BoneRole.LowerLegR, BoneRole.FootL, BoneRole.FootR }
            .Sum(role => rig[map.RoleToBone[role]].RestLocal.Pos.Length());
        MotionScale = LegLength(target, targetMap) / LegLength(source, sourceMap);
        if (!float.IsFinite(MotionScale) || MotionScale <= 0) throw new ArgumentException("Invalid humanoid proportions.");
        var definitions = target.Bones.Select(b => new BoneDefinition(b.Name,
            b.ParentIndex < 0 ? null : target[b.ParentIndex].Name, b.RestLocal)).ToList();
        // Graph-only IK, weapon and extra spine bones are retained, never substituted for skin bones.
        foreach (var bone in source.Bones)
        {
            if (names.ContainsKey(bone.Name)) continue;
            if (lookup.ContainsKey(bone.Name)) throw new ArgumentException("Conflicting graph bone: " + bone.Name);
            names.Add(bone.Name, bone.Name);
            definitions.Add(new BoneDefinition(bone.Name, bone.ParentIndex < 0 ? null : names[source[bone.ParentIndex].Name],
                new XForm(bone.RestLocal.Pos * MotionScale, bone.RestLocal.Rot)));
        }
        Target = SkeletonModel.Create(definitions);
        var mapped = new MappingResult(targetMap.ProfileName, targetMap.Source);
        foreach (var (role, index) in targetMap.RoleToBone) mapped.RoleToBone.Add(role, Target.IndexOf(target[index].Name));
        roleBones = mapped.RoleToBone.Values.ToHashSet();
        solver = new RuntimePoseRetargeter(source, sourceMap, TargetRig.FromSkeleton(Target, mapped));
        sourceIndices = Enumerable.Repeat(-1, Target.Count).ToArray();
        foreach (var bone in source.Bones) sourceIndices[Target.IndexOf(names[bone.Name])] = bone.Index;
        sourceParents = sourceIndices.Select(i => i < 0 ? -1 : source[i].ParentIndex).ToArray();
        foreach (var bone in Target.Bones)
        {
            if (bone.ParentIndex < 0 || sourceIndices[bone.ParentIndex] < 0) continue;
            var parent = sourceIndices[bone.ParentIndex];
            for (var ancestor = sourceParents[bone.Index]; ancestor >= 0; ancestor = source[ancestor].ParentIndex)
                if (ancestor == parent) { sourceParents[bone.Index] = parent; break; }
        }
        reference = TransferAbsolute(source.Bones.Select(b => b.RestLocal).ToArray());
    }

    /// <summary>Modern compiled additive sequences may omit the legacy delta flag.
    /// Their limb translations are offsets near zero, not complete anatomical lengths.</summary>
    public bool IsDeltaPose(XForm[] pose)
    {
        if (pose.Length != Source.Count) throw new ArgumentException("Wrong source bone count.");
        var offsets = limbEnds.Count(i => Source[i].RestLocal.Pos.LengthSquared() > .0001f
            && pose[i].Pos.LengthSquared() < Source[i].RestLocal.Pos.LengthSquared() * .0625f);
        return offsets >= limbEnds.Length * 3 / 4;
    }

    static MappingResult Map(SkeletonModel skeleton)
    {
        var map = Retargeter.ResolveMapping(skeleton).Map;
        // A known rig family can still have extra/renamed joints (for example head_0).
        foreach (var (role, bone) in AutoMapper.Map(skeleton).RoleToBone)
            if (!map.RoleToBone.ContainsKey(role) && !map.RoleToBone.ContainsValue(bone)) map.RoleToBone.Add(role, bone);
        return map;
    }

    /// <summary>Retarget absolute poses and additive deltas separately; a zero delta stays zero.</summary>
    public XForm[] Transfer(XForm[] pose, bool delta = false)
    {
        if (pose.Length != Source.Count) throw new ArgumentException("Wrong source bone count.");
        if (!delta) return TransferAbsolute(pose);
        var absolute = new XForm[pose.Length];
        for (var i = 0; i < pose.Length; i++)
            absolute[i] = new XForm(Source[i].RestLocal.Pos + pose[i].Pos,
                Quaternion.Normalize(Source[i].RestLocal.Rot * pose[i].Rot));
        var result = TransferAbsolute(absolute);
        for (var i = 0; i < result.Length; i++)
            result[i] = new XForm(result[i].Pos - reference[i].Pos,
                Quaternion.Normalize(Quaternion.Conjugate(reference[i].Rot) * result[i].Rot));
        return result;
    }

    XForm[] TransferAbsolute(XForm[] pose)
    {
        var result = new XForm[Target.Count];
        solver.Retarget(pose, result);
        for (var i = 0; i < result.Length; i++)
        {
            if (roleBones.Contains(i)) continue;
            var s = sourceIndices[i];
            if (s < 0) { result[i] = Target[i].RestLocal; continue; }
            var sp = sourceParents[i];
            var tp = Target[i].ParentIndex;
            var local = pose[s];
            var rest = Source[s].RestLocal;
            // A matching helper can have fewer ancestors on the target. Fold in the
            // skipped source drivers (e.g. an animated weapon pivot above a grip).
            for (var ancestor = Source[s].ParentIndex; ancestor != sp; ancestor = Source[ancestor].ParentIndex)
            {
                local = XForm.Compose(pose[ancestor], local);
                rest = XForm.Compose(Source[ancestor].RestLocal, rest);
            }
            var basis = sp < 0 || tp < 0 ? Quaternion.Identity
                : Quaternion.Normalize(Quaternion.Conjugate(Target.RestWorld[tp].Rot) * Source.RestWorld[sp].Rot);
            var delta = Quaternion.Normalize(local.Rot * Quaternion.Conjugate(rest.Rot));
            result[i] = new XForm(Target[i].RestLocal.Pos + NVector3.Transform(local.Pos - rest.Pos, basis) * MotionScale,
                Quaternion.Normalize(basis * delta * Quaternion.Conjugate(basis) * Target[i].RestLocal.Rot));
        }
        return result;
    }
}
