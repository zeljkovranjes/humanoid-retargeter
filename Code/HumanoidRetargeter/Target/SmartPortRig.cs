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
    // Existing target sockets far from the grip, fitted to the knuckles per frame (position only).
    readonly HashSet<int> refitSockets = new();
    readonly Dictionary<int, Palm> palms = new();
    readonly List<(int SourceGoal, int SourceEnd, int TargetGoal, int TargetEnd, bool CrossHand, bool Rigid)> ikGoals = new();

    /// <param name="targetRoles">Target bones for roles the mapper cannot find (e.g. a rig whose head is replaced by
    /// another part); each overrides whatever the bone or role was mapped to.</param>
    public SmartPortRig(SkeletonModel source, SkeletonModel target, IReadOnlyDictionary<string, string>? ikTargets = null,
        IReadOnlyCollection<string>? attachmentBones = null, IReadOnlyDictionary<BoneRole, string>? targetRoles = null)
    {
        Source = source;
        foreach (var bone in source.Bones.Concat(target.Bones))
            if (!float.IsFinite(bone.RestLocal.Pos.LengthSquared()) || !float.IsFinite(bone.RestLocal.Rot.LengthSquared())
                || bone.RestLocal.Rot.LengthSquared() < .0001f) throw new ArgumentException("Invalid bind transform: " + bone.Name);
        var sourceMap = Map(source);
        var targetMap = Map(target);
        foreach (var (role, name) in targetRoles ?? new Dictionary<BoneRole, string>())
        {
            var index = target.IndexOf(name);
            if (index < 0) throw new ArgumentException($"Role {role} names a bone the target does not have: {name}");
            foreach (var other in targetMap.RoleToBone.Where(p => p.Value == index).Select(p => p.Key).ToArray())
                targetMap.RoleToBone.Remove(other);
            targetMap.RoleToBone[role] = index;
        }
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
            var knuckles = new[] { "IndexProx", "MiddleProx", "RingProx" }.Select(n => Enum.Parse<BoneRole>(n + side))
                .Where(r => sourceMap.RoleToBone.ContainsKey(r) && mapped.RoleToBone.ContainsKey(r)).ToArray();
            if (knuckles.Length < 2) continue; // Insufficient hand geometry: keep the existing transfer.
            var sp = knuckles.Select(r => source.RestWorld[sourceMap.RoleToBone[r]].Pos)
                .Aggregate(NVector3.Zero, (a, b) => a + b) / knuckles.Length;
            var tp = knuckles.Select(r => Target.RestWorld[mapped.RoleToBone[r]].Pos)
                .Aggregate(NVector3.Zero, (a, b) => a + b) / knuckles.Length;
            // An existing socket (the target's own hold_R) is authoritative anywhere on the hand,
            // hand-fitted or not. One lying clearly outside the hand - further from the knuckles
            // than the whole hand reaches, e.g. a socket left from a much larger rig - holds the
            // weapon in the air, so its animated position is fitted to the knuckles like a copied
            // one. Its bind and rotation are kept.
            var fingers = new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" };
            var reach = mapped.RoleToBone
                .Where(r => r.Key.ToString().EndsWith(side, StringComparison.Ordinal) && fingers.Any(f => r.Key.ToString().StartsWith(f, StringComparison.Ordinal)))
                .Select(r => NVector3.Distance(Target.RestWorld[r.Value].Pos, Target.RestWorld[th].Pos))
                .DefaultIfEmpty(0).Max();
            var offGrip = attachmentBones.Select(source.IndexOf).Where(i => i >= 0 && source[i].ParentIndex >= 0)
                .Select(i => (Source: i, Target: Target.IndexOf(names[source[i].Name])))
                .Where(x => x.Target >= 0 && !copiedHelpers.Contains(x.Target)
                    && source[x.Source].ParentIndex == sh && Target[x.Target].ParentIndex == th)
                .Where(x => reach > 1e-4f && NVector3.Distance(Target.RestWorld[x.Target].Pos, tp) > reach)
                .Select(x => x.Target).ToArray();
            // The target's own in-hand socket (neither copied nor refitted) is where it holds a weapon.
            var existing = attachmentBones.Select(source.IndexOf).Where(i => i >= 0 && source[i].ParentIndex == sh)
                .Where(i => Target.IndexOf(names[source[i].Name]) is var t && t >= 0 && Target[t].ParentIndex == th
                    && !copiedHelpers.Contains(t) && !offGrip.Contains(t)).ToArray();
            if (sockets.Length == 0 && offGrip.Length == 0 && existing.Length == 0) continue;
            refitSockets.UnionWith(offGrip);
            handSockets.UnionWith(offGrip);
            // A hand that receives a copied socket holds an unscaled weapon the way the source
            // hand does: same palm orientation, and a socket placed for this hand's size.
            var sourceFrame = PalmFrame(source.RestWorld[sh], knuckles.Select(r => source.RestWorld[sourceMap.RoleToBone[r]].Pos).ToArray());
            var targetFrame = PalmFrame(Target.RestWorld[th], knuckles.Select(r => Target.RestWorld[mapped.RoleToBone[r]].Pos).ToArray());
            var sourceSize = NVector3.Distance(source.RestWorld[sh].Pos, sp);
            var handScale = sourceSize > 1e-4f ? NVector3.Distance(Target.RestWorld[th].Pos, tp) / sourceSize : 0;
            var fitted = sockets.Length > 0 && sourceFrame is not null && targetFrame is not null && handScale > 1e-4f;
            palms.Add(sh, new Palm(th, mapped.RoleToBone[Enum.Parse<BoneRole>("UpperArm" + side)],
                mapped.RoleToBone[Enum.Parse<BoneRole>("LowerArm" + side)],
                source.RestWorld[sh].Inverse().TransformPoint(sp), Target.RestWorld[th].Inverse().TransformPoint(tp),
                fitted ? handScale : MotionScale,
                fitted ? Quaternion.Normalize(sourceFrame!.Value * Quaternion.Conjugate(targetFrame!.Value)) : null,
                fitted ? sockets.Select(i => sourceIndices[i]).MinBy(i => NVector3.Distance(source.RestWorld[i].Pos, sp))
                    : sockets.Length == 0 && existing.Length > 0 ? existing.MinBy(i => NVector3.Distance(source.RestWorld[i].Pos, sp)) : -1,
                fitted));
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
                var crossHand = parent != sourceEnd && hands.Contains(parent) && hands.Contains(sourceEnd);
                // A goal held relative to the other hand's weapon socket keeps the support grip on that
                // (unscaled) weapon, whether the goal bone was copied or already existed on the target.
                var rigid = crossHand && palms.TryGetValue(parent, out var held) && held.SourceSocket >= 0 && palms.ContainsKey(sourceEnd);
                ikGoals.Add((sourceGoal, sourceEnd, targetGoal, Target.IndexOf(names[end]),
                    copiedHelpers.Contains(targetGoal) && crossHand, rigid));
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
        if (fitGoals && (ikGoals.Count > 0 || copiedHelpers.Count > 0 || refitSockets.Count > 0))
        {
            var sourceWorld = World(Source, pose);
            AlignPalms(result, sourceWorld);
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
                    if (handSockets.Contains(bone.Index)) frame = SocketFrame(s, sourceWorld, targetWorld);
                    result[bone.Index] = parent < 0 ? frame : XForm.ToLocal(targetWorld[parent], frame);
                }
                else if (refitSockets.Contains(bone.Index) && parent >= 0)
                {
                    // An off-grip existing socket: its own animated axes, the source grip's place.
                    var s = sourceIndices[bone.Index];
                    var frame = XForm.Compose(targetWorld[parent], result[bone.Index]);
                    frame.Pos = targetWorld[parent].Pos + (sourceWorld[s].Pos - sourceWorld[Source[s].ParentIndex].Pos) * MotionScale
                        + PalmShift(Source[s].ParentIndex, sourceWorld, targetWorld);
                    result[bone.Index] = XForm.ToLocal(targetWorld[parent], frame);
                }
                targetWorld[bone.Index] = parent < 0 ? result[bone.Index] : XForm.Compose(targetWorld[parent], result[bone.Index]);
            }
            foreach (var (sourceGoal, sourceEnd, targetGoal, targetEnd, crossHand, rigid) in ikGoals)
            {
                // Goals are effector frames, not skin joints: retaining their target bind
                // offsets can lock a hand in mid-air when the rigs have different rest poses.
                var offset = XForm.ToLocal(sourceWorld[sourceEnd], sourceWorld[sourceGoal]);
                offset.Pos *= MotionScale;
                var goal = XForm.Compose(targetWorld[targetEnd], offset);
                // A newly copied support-hand goal is anchored to the other hand.
                // Keep that grip separation; fitting it back to the free arm loses contact.
                if (crossHand || rigid) goal.Pos = SupportGoal(Source[sourceGoal].ParentIndex, sourceEnd, targetWorld[targetGoal].Pos,
                    goal.Rot, sourceWorld, targetWorld);
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

    /// <param name="Scale">Socket offset scale from the knuckles: hand size for a hand fitted to hold a copied socket.</param>
    /// <param name="Align">Target hand world rotation relative to the source hand's, so both palms face alike.</param>
    /// <param name="SourceSocket">The grip socket nearest the source knuckles, or -1.</param>
    /// <param name="Copied">The socket was copied and is fitted here; otherwise the target's own socket is kept.</param>
    sealed record Palm(int TargetHand, int Upper, int Lower, NVector3 SourcePalm, NVector3 TargetPalm,
        float Scale, Quaternion? Align, int SourceSocket, bool Copied);

    /// <summary>Hand-local palm axes: wrist to knuckle centre, then across the knuckles. Bone-axis conventions do not matter.</summary>
    static Quaternion? PalmFrame(XForm hand, NVector3[] knuckles)
    {
        var centre = knuckles.Aggregate(NVector3.Zero, (a, b) => a + b) / knuckles.Length;
        var along = centre - hand.Pos;
        var across = knuckles[0] - knuckles[^1];
        if (along.LengthSquared() < 1e-8f) return null;
        along = NVector3.Normalize(along);
        across -= along * NVector3.Dot(across, along);
        if (across.LengthSquared() < 1e-8f) return null;
        across = NVector3.Normalize(across);
        var normal = NVector3.Cross(along, across);
        var world = Quaternion.CreateFromRotationMatrix(new Matrix4x4(along.X, along.Y, along.Z, 0,
            across.X, across.Y, across.Z, 0, normal.X, normal.Y, normal.Z, 0, 0, 0, 0, 1));
        return Quaternion.Normalize(Quaternion.Conjugate(hand.Rot) * world);
    }

    // A copied socket keeps its source model-space axes (the weapon's aim). Turn the fitted
    // hand's palm to the source's so the weapon lies in it; bone lengths are unchanged.
    void AlignPalms(XForm[] pose, XForm[] sourceWorld)
    {
        var world = World(Target, pose);
        foreach (var (sourceHand, palm) in palms)
        {
            var parent = Target[palm.TargetHand].ParentIndex;
            if (palm.Align is not { } align || parent < 0) continue;
            pose[palm.TargetHand] = new XForm(pose[palm.TargetHand].Pos,
                Quaternion.Normalize(Quaternion.Conjugate(world[parent].Rot) * sourceWorld[sourceHand].Rot * align));
        }
    }

    // Preserve the source socket's relation to the knuckles, not just to the wrist.
    // Newly copied attachment bones are fitted; existing target sockets stay authoritative
    // unless they sit clearly off the grip (see the constructor).
    NVector3 PalmShift(int sourceHand, XForm[] sourceWorld, XForm[] targetWorld)
        => palms.TryGetValue(sourceHand, out var palm)
            ? targetWorld[palm.TargetHand].TransformVector(palm.TargetPalm)
                - sourceWorld[sourceHand].TransformVector(palm.SourcePalm) * MotionScale
            : NVector3.Zero;

    /// <summary>A copied hand socket: source model-space axes, placed from the target knuckles.
    /// The target's own socket stays where its animation puts it.</summary>
    XForm SocketFrame(int sourceSocket, XForm[] sourceWorld, XForm[] targetWorld)
    {
        var hand = Source[sourceSocket].ParentIndex;
        var palm = palms[hand];
        if (!palm.Copied && palm.SourceSocket == sourceSocket) return targetWorld[Target.IndexOf(names[Source[sourceSocket].Name])];
        var frame = sourceWorld[sourceSocket];
        frame.Pos = targetWorld[palm.TargetHand].TransformPoint(palm.TargetPalm)
            + (frame.Pos - sourceWorld[hand].TransformPoint(palm.SourcePalm)) * palm.Scale;
        return frame;
    }

    /// <summary>Support wrist position for a cross-hand goal anchored to the other hand.</summary>
    /// <remarks>A held weapon is never scaled with the character. When the anchor hand carries a
    /// fitted grip socket, the support hand's grip (its own socket, else its knuckles) keeps the
    /// source's place in that socket's frame instead of a body-scaled separation, so both hands
    /// stay on the same weapon.</remarks>
    NVector3 SupportGoal(int anchor, int end, NVector3 copied, Quaternion rotation, XForm[] sourceWorld, XForm[] targetWorld)
    {
        if (!palms.TryGetValue(anchor, out var held) || held.SourceSocket < 0 || !palms.TryGetValue(end, out var support))
            return copied + PalmShift(anchor, sourceWorld, targetWorld) - PalmShift(end, sourceWorld, targetWorld);
        var grip = XForm.ToLocal(sourceWorld[held.SourceSocket], support.SourceSocket >= 0 ? sourceWorld[support.SourceSocket]
            : new XForm(sourceWorld[end].TransformPoint(support.SourcePalm), Quaternion.Identity)).Pos;
        var place = SocketFrame(held.SourceSocket, sourceWorld, targetWorld).TransformPoint(grip);
        var hand = targetWorld[support.TargetHand];
        var contact = support.SourceSocket >= 0 ? SocketFrame(support.SourceSocket, sourceWorld, targetWorld).Pos
            : hand.TransformPoint(support.TargetPalm);
        return place - NVector3.Transform(contact - hand.Pos, rotation * Quaternion.Conjugate(hand.Rot));
    }

    void FitGripReach(XForm[] pose, XForm[] sourceWorld)
    {
        if (palms.Count != 2) return;
        var contact = ikGoals.FirstOrDefault(g => (g.CrossHand || g.Rigid)
            && NVector3.Distance(sourceWorld[g.SourceGoal].Pos, sourceWorld[g.SourceEnd].Pos) < .01f);
        if (!contact.CrossHand && !contact.Rigid) return;
        var anchor = Source[contact.SourceGoal].ParentIndex;
        var hands = new[] { palms[anchor], palms[contact.SourceEnd] };
        var world = World(Target, pose);
        var anchored = world[hands[0].TargetHand].Pos;
        var turn = XForm.ToLocal(sourceWorld[contact.SourceEnd], sourceWorld[contact.SourceGoal]).Rot;
        var goals = new[] { anchored, SupportGoal(anchor, contact.SourceEnd,
            anchored + (sourceWorld[contact.SourceGoal].Pos - sourceWorld[anchor].Pos) * MotionScale,
            Quaternion.Normalize(world[hands[1].TargetHand].Rot * turn), sourceWorld, world) };
        var radii = hands.Select(h => (NVector3.Distance(world[h.Upper].Pos, world[h.Lower].Pos)
            + NVector3.Distance(world[h.Lower].Pos, world[h.TargetHand].Pos)) * .98f).ToArray();
        // Keep the source support arm's bend reserve for graph blends and recoil. The graph's
        // locomotion layers move the shoulders under a held weapon; an arm already more extended
        // than the source's runs out of reach while walking, even when the clip itself fits.
        var sourceUpper = sourceIndices[hands[1].Upper];
        var sourceLower = sourceIndices[hands[1].Lower];
        var sourceReach = NVector3.Distance(sourceWorld[sourceUpper].Pos, sourceWorld[sourceLower].Pos)
            + NVector3.Distance(sourceWorld[sourceLower].Pos, sourceWorld[contact.SourceEnd].Pos);
        if (sourceReach < .0001f) return;
        var sourceDistance = NVector3.Distance(sourceWorld[sourceUpper].Pos, sourceWorld[contact.SourceEnd].Pos);
        var extension = sourceDistance / sourceReach;
        var arm = radii[1] / .98f;
        radii[1] *= MathF.Min(1, extension / .98f);
        // That shoulder motion scales with the body, not the arm: short arms also need the
        // source's slack at body scale, or walking pulls the support hand off the weapon.
        radii[1] = MathF.Min(radii[1], MathF.Max(arm * .5f, arm - (sourceReach - sourceDistance) * MotionScale));
        if (NVector3.Distance(goals[1], world[hands[1].Upper].Pos) <= radii[1] + .001f) return;
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
