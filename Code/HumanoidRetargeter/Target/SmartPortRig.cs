#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using HumanoidRetargeter.Mapping;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Solve;
using HumanoidRetargeter.Cleanup;
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
    readonly HashSet<int> copiedHelpers;
    readonly HashSet<int> handSockets = new();
    readonly Dictionary<int, (int TargetHand, int Upper, int Lower, NVector3 SourcePalm, NVector3 TargetPalm)> palms = new();
    readonly List<(int SourceGoal, int SourceEnd, int TargetGoal, int TargetEnd, bool CrossHand)> ikGoals = new();

    public SmartPortRig(SkeletonModel source, SkeletonModel target, IReadOnlyDictionary<string, string>? ikTargets = null,
        IReadOnlyCollection<string>? attachmentBones = null)
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
        copiedHelpers = Target.Bones.Where(b => target.IndexOf(b.Name) < 0).Select(b => b.Index).ToHashSet();
        var mapped = new MappingResult(targetMap.ProfileName, targetMap.Source);
        foreach (var (role, index) in targetMap.RoleToBone) mapped.RoleToBone.Add(role, Target.IndexOf(target[index].Name));
        roleBones = mapped.RoleToBone.Values.ToHashSet();
        solver = new RuntimePoseRetargeter(source, sourceMap, TargetRig.FromSkeleton(Target, mapped));
        sourceIndices = Enumerable.Repeat(-1, Target.Count).ToArray();
        foreach (var bone in source.Bones) sourceIndices[Target.IndexOf(names[bone.Name])] = bone.Index;
        if (attachmentBones is not null)
        foreach (var side in new[] { "L", "R" })
        {
            var handRole = Enum.Parse<BoneRole>("Hand" + side);
            var sh = sourceMap.RoleToBone[handRole];
            var th = mapped.RoleToBone[handRole];
            int SocketRoot(int index)
            {
                while (index >= 0)
                {
                    // An existing fitted socket (or intermediate helper) is authoritative.
                    if (!copiedHelpers.Contains(Target.IndexOf(names[source[index].Name]))) return -1;
                    if (source[index].ParentIndex == sh) return index;
                    index = source[index].ParentIndex;
                }
                return -1;
            }
            var sockets = attachmentBones.Select(source.IndexOf).Select(SocketRoot).Where(i => i >= 0)
                .Select(i => Target.IndexOf(names[source[i].Name])).ToArray();
            if (sockets.Length == 0) continue;
            var knuckles = new[] { "IndexProx", "MiddleProx", "RingProx" }.Select(n => Enum.Parse<BoneRole>(n + side))
                .Where(r => sourceMap.RoleToBone.ContainsKey(r) && mapped.RoleToBone.ContainsKey(r)).ToArray();
            if (knuckles.Length < 2) continue; // Insufficient hand geometry: keep the existing transfer.
            var sp = knuckles.Select(r => source.RestWorld[sourceMap.RoleToBone[r]].Pos)
                .Aggregate(NVector3.Zero, (a, b) => a + b) / knuckles.Length;
            var tp = knuckles.Select(r => Target.RestWorld[mapped.RoleToBone[r]].Pos)
                .Aggregate(NVector3.Zero, (a, b) => a + b) / knuckles.Length;
            palms.Add(sh, (th, mapped.RoleToBone[Enum.Parse<BoneRole>("UpperArm" + side)],
                mapped.RoleToBone[Enum.Parse<BoneRole>("LowerArm" + side)],
                source.RestWorld[sh].Inverse().TransformPoint(sp), Target.RestWorld[th].Inverse().TransformPoint(tp)));
            handSockets.UnionWith(sockets);
        }
        sourceParents = sourceIndices.Select(i => i < 0 ? -1 : source[i].ParentIndex).ToArray();
        foreach (var bone in Target.Bones)
        {
            if (bone.ParentIndex < 0 || sourceIndices[bone.ParentIndex] < 0) continue;
            var parent = sourceIndices[bone.ParentIndex];
            for (var ancestor = sourceParents[bone.Index]; ancestor >= 0; ancestor = source[ancestor].ParentIndex)
                if (ancestor == parent) { sourceParents[bone.Index] = parent; break; }
        }
        if (ikTargets is not null)
            foreach (var (goal, end) in ikTargets)
            {
                var sourceGoal = source.IndexOf(goal);
                var sourceEnd = source.IndexOf(end);
                if (sourceGoal < 0 || sourceEnd < 0) throw new ArgumentException("IK goal or effector is missing from the source skeleton.");
                var targetGoal = Target.IndexOf(names[goal]);
                if (roleBones.Contains(targetGoal)) continue; // An actual skin joint can also be used as an IK target.
                var hands = new[] { sourceMap.RoleToBone[BoneRole.HandL], sourceMap.RoleToBone[BoneRole.HandR] };
                var parent = Source[sourceGoal].ParentIndex;
                ikGoals.Add((sourceGoal, sourceEnd, targetGoal, Target.IndexOf(names[end]),
                    copiedHelpers.Contains(targetGoal) && parent != sourceEnd && hands.Contains(parent) && hands.Contains(sourceEnd)));
            }
        reference = TransferAbsolute(source.Bones.Select(b => b.RestLocal).ToArray(), false);
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
        // Additive goal channels act on an already fitted grip. Re-solving them
        // against the bind pose invents goal translations during arm-only recoil.
        var result = TransferAbsolute(absolute, false);
        for (var i = 0; i < result.Length; i++)
            result[i] = new XForm(result[i].Pos - reference[i].Pos,
                Quaternion.Normalize(Quaternion.Conjugate(reference[i].Rot) * result[i].Rot));
        return result;
    }

    XForm[] TransferAbsolute(XForm[] pose, bool fitGoals = true)
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
        if (fitGoals && (ikGoals.Count > 0 || copiedHelpers.Count > 0))
        {
            var sourceWorld = World(Source, pose);
            FitGripReach(result, sourceWorld);
            var targetWorld = new XForm[Target.Count];
            foreach (var bone in Target.Bones)
            {
                var parent = bone.ParentIndex;
                if (copiedHelpers.Contains(bone.Index))
                {
                    // Newly copied graph frames have no fitted skin bind to preserve.
                    // Keep their animated model-space axes and offset from the mapped
                    // parent; source local axes can be unrelated to a custom hand's axes.
                    var s = sourceIndices[bone.Index];
                    var frame = sourceWorld[s];
                    frame.Pos = parent < 0 ? frame.Pos * MotionScale : targetWorld[parent].Pos
                        + (frame.Pos - sourceWorld[Source[s].ParentIndex].Pos) * MotionScale;
                    if (handSockets.Contains(bone.Index)) frame.Pos += PalmShift(Source[s].ParentIndex, sourceWorld, targetWorld);
                    result[bone.Index] = parent < 0 ? frame : XForm.ToLocal(targetWorld[parent], frame);
                }
                targetWorld[bone.Index] = parent < 0 ? result[bone.Index] : XForm.Compose(targetWorld[parent], result[bone.Index]);
            }
            foreach (var (sourceGoal, sourceEnd, targetGoal, targetEnd, crossHand) in ikGoals)
            {
                // Goals are effector frames, not skin joints: retaining their target bind
                // offsets can lock a hand in mid-air when the rigs have different rest poses.
                var offset = XForm.ToLocal(sourceWorld[sourceEnd], sourceWorld[sourceGoal]);
                offset.Pos *= MotionScale;
                var goal = XForm.Compose(targetWorld[targetEnd], offset);
                // A newly copied support-hand goal is anchored to the other hand.
                // Keep that grip separation; fitting it back to the free arm loses contact.
                if (crossHand) goal.Pos = targetWorld[targetGoal].Pos
                    + PalmShift(Source[sourceGoal].ParentIndex, sourceWorld, targetWorld) - PalmShift(sourceEnd, sourceWorld, targetWorld);
                var parent = Target[targetGoal].ParentIndex;
                result[targetGoal] = parent < 0 ? goal : XForm.ToLocal(targetWorld[parent], goal);
            }
        }
        return result;
    }

    static XForm[] World(SkeletonModel skeleton, XForm[] pose)
    {
        var world = new XForm[skeleton.Count];
        foreach (var bone in skeleton.Bones)
            world[bone.Index] = bone.ParentIndex < 0 ? pose[bone.Index] : XForm.Compose(world[bone.ParentIndex], pose[bone.Index]);
        return world;
    }

    // Preserve the source socket's relation to the knuckles, not just to the wrist.
    // Only newly copied attachment bones are fitted; existing target sockets stay authoritative.
    NVector3 PalmShift(int sourceHand, XForm[] sourceWorld, XForm[] targetWorld)
        => palms.TryGetValue(sourceHand, out var palm)
            ? targetWorld[palm.TargetHand].TransformVector(palm.TargetPalm)
                - sourceWorld[sourceHand].TransformVector(palm.SourcePalm) * MotionScale
            : NVector3.Zero;

    void FitGripReach(XForm[] pose, XForm[] sourceWorld)
    {
        if (palms.Count != 2) return;
        var contact = ikGoals.FirstOrDefault(g => g.CrossHand
            && NVector3.Distance(sourceWorld[g.SourceGoal].Pos, sourceWorld[g.SourceEnd].Pos) < .01f);
        if (!contact.CrossHand) return;
        var anchor = Source[contact.SourceGoal].ParentIndex;
        var hands = new[] { palms[anchor], palms[contact.SourceEnd] };
        var world = World(Target, pose);
        var anchored = world[hands[0].TargetHand].Pos;
        var goals = new[] { anchored, anchored + (sourceWorld[contact.SourceGoal].Pos - sourceWorld[anchor].Pos) * MotionScale
            + PalmShift(anchor, sourceWorld, world) - PalmShift(contact.SourceEnd, sourceWorld, world) };
        var radii = hands.Select(h => (NVector3.Distance(world[h.Upper].Pos, world[h.Lower].Pos)
            + NVector3.Distance(world[h.Lower].Pos, world[h.TargetHand].Pos)) * .98f).ToArray();
        if (NVector3.Distance(goals[1], world[hands[1].Upper].Pos) <= radii[1]) return;
        // Keep the source support arm's bend reserve for graph blends and recoil.
        // Merely fitting an almost straight arm leaves no room for locomotion layers.
        var sourceUpper = sourceIndices[hands[1].Upper];
        var sourceLower = sourceIndices[hands[1].Lower];
        var sourceReach = NVector3.Distance(sourceWorld[sourceUpper].Pos, sourceWorld[sourceLower].Pos)
            + NVector3.Distance(sourceWorld[sourceLower].Pos, sourceWorld[contact.SourceEnd].Pos);
        if (sourceReach < .0001f) return;
        var extension = NVector3.Distance(sourceWorld[sourceUpper].Pos, sourceWorld[contact.SourceEnd].Pos) / sourceReach;
        radii[1] *= MathF.Min(1, extension / .98f);
        // Move the shared grip into both arms' reach. Leave a small elbow margin,
        // like the existing IK solver, and never stretch or change local translations.
        var shift = NVector3.Zero;
        for (var step = 0; step < 8; step++)
        for (var i = 0; i < hands.Length; i++)
        {
            var offset = goals[i] + shift - world[hands[i].Upper].Pos;
            var distance = offset.Length();
            if (distance > radii[i]) shift -= offset * (1 - radii[i] / distance);
        }
        // Some source spans cannot fit this body at all. Do not distort its arms.
        if (hands.Where((h, i) => NVector3.Distance(goals[i] + shift, world[h.Upper].Pos) > radii[i] + .001f).Any()) return;
        for (var i = 0; i < hands.Length; i++)
        {
            var h = hands[i];
            world = World(Target, pose);
            var ik = TwoBoneIk.Solve(world[h.Upper].Pos, world[h.Lower].Pos, world[h.TargetHand].Pos, goals[i] + shift, soften: 0);
            EffectorIk.ApplyWorldDeltas(pose, Target, h.Upper, h.Lower, h.TargetHand,
                ik.UpperWorldDelta, ik.LowerWorldDelta, world);
        }
    }
}
