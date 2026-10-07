using System.Numerics;
using HumanoidRetargeter.Core.Cleanup;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Solve;
using HumanoidRetargeter.Core.Target;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;
using Xunit;
using HumanoidRetargeter.Core;

namespace HumanoidRetargeter.Tests.Solve;

/// <summary>
/// Local-only head-attitude quality gate against the REAL user-reported repro file in
/// <c>dev/corpus/todo/</c> (not committed; the test silently passes when the file is
/// absent). User report: Defenses.fbx "the head is not in the correct position, like it's
/// looking kinda up at an angle" — the rig's bind rest is a posed fighting stance whose
/// rest head leans 40.7° forward / 16.9° sideways (vs −3..27° / ≤3° on every neutral-rest
/// corpus rig), so the head's rest-relative delta replay constantly un-tipped the output
/// head by that pose (measured −12.3° mean pitch vs the source plus a ~39° carried-yaw
/// error). The fix: the solver's posed-rest-head fallback switches the head to
/// <see cref="RoleTransferMode.AbsoluteDirection"/> (gaze follows the source absolutely).
/// </summary>
public class HeadGazeReproTests
{
    private const float Rad2Deg = 180f / MathF.PI;

    /// <summary>The full pipeline must track the source's head attitude (the rig's own rest
    /// neck→head direction carried by the head bone's world delta, measured as forward lean
    /// from character up) — mean error vs the source ≤ 3° on stance holds (both feet
    /// planted), was −12.2° "looking up" before the posed-rest gaze fallback.</summary>
    [Fact] // skipped (silently green) when the local corpus file is not present
    public void Defenses_HeadAttitude_TracksSource_Within3Degrees()
    {
        var path = RepoFile("dev", "corpus", "todo", "Defenses.fbx");
        if (!File.Exists(path))
            return;

        var bytes = File.ReadAllBytes(path);
        var scene = Retargeter.ImportSource(bytes, "Defenses.fbx");
        var (map, _) = Retargeter.ResolveMapping(scene.Skeleton);
        var target = RetargetTargetSpec.SboxDefault(
            File.ReadAllText(RepoFile("Assets", "data", "humanoid_retargeter", "target_rig_sbox.json")));

        var result = Retargeter.Convert(new RetargetRequest
        {
            SourceData = bytes,
            SourceFileName = "Defenses.fbx",
            MappingOverride = map,
        }, target);
        var tgtFrames = result.Clips.First(c => c.Success).SolvedFrames!;
        var rig = target.Rig;

        var srcSkel = scene.Skeleton;
        var tgtSkel = rig.Skeleton;
        var tgtMap = rig.ToMappingResult();
        var (srcNorm, _) = RestNormalizer.Normalize(srcSkel, map);
        var (tgtNorm, _) = RestNormalizer.Normalize(tgtSkel, tgtMap);
        var srcCanon = CanonicalFrames.Build(srcSkel, map, srcNorm.WorldRest);
        var tgtCanon = CanonicalFrames.Build(tgtSkel, tgtMap, tgtNorm.WorldRest);

        var clip = scene.Clips[0];
        var holds = StanceHoldFrames(scene, map, srcCanon.CharacterUp, clip);
        Assert.True(holds.Count >= 100, $"too few stance-hold frames ({holds.Count})");

        var srcLean = HeadLeans(
            clip.Frames, srcSkel, map.RoleToBone[BoneRole.Neck], map.RoleToBone[BoneRole.Head],
            srcNorm.WorldRest, srcCanon.CharacterForward, srcCanon.CharacterUp);
        var tgtLean = HeadLeans(
            tgtFrames, tgtSkel, rig.BoneForRole(BoneRole.Neck)!.Value, rig.BoneForRole(BoneRole.Head)!.Value,
            tgtNorm.WorldRest, tgtCanon.CharacterForward, tgtCanon.CharacterUp);

        var meanErr = holds.Average(f => tgtLean[f] - srcLean[f]);
        var maxErr = holds.Max(f => MathF.Abs(tgtLean[f] - srcLean[f]));
        Assert.True(MathF.Abs(meanErr) <= 3f,
            $"head attitude error vs source: mean {meanErr:F2} deg on stance holds "
            + "(the posed-rest gaze fallback should track the source's head pitch)");
        Assert.True(maxErr <= 6f, $"head attitude error vs source: max {maxErr:F2} deg");
    }

    // ---------------------------------------------------------------- metrics

    /// <summary>Frames where BOTH source feet are planted (Kovar detection on the source).</summary>
    private static List<int> StanceHoldFrames(
        SourceScene scene, MappingResult map, Vector3 srcUp, Clip clip)
    {
        int? Bone(BoneRole r) => map.RoleToBone.TryGetValue(r, out var b) ? b : null;
        FootChain Chain(BoneRole u, BoneRole l, BoneRole f, BoneRole t) => new()
        {
            Hip = Bone(u)!.Value,
            Knee = Bone(l)!.Value,
            Ankle = Bone(f)!.Value,
            Toe = Bone(t),
        };
        var (plantsL, plantsR) = FootPlant.DetectPlantIntervals(
            clip.Frames, scene.Skeleton,
            Chain(BoneRole.UpperLegL, BoneRole.LowerLegL, BoneRole.FootL, BoneRole.ToeL),
            Chain(BoneRole.UpperLegR, BoneRole.LowerLegR, BoneRole.FootR, BoneRole.ToeR),
            Vector3.Normalize(srcUp), clip.Fps);

        var n = clip.FrameCount;
        var left = new bool[n];
        var right = new bool[n];
        foreach (var p in plantsL)
            for (int f = Math.Max(p.Start, 0); f <= p.End && f < n; f++) left[f] = true;
        foreach (var p in plantsR)
            for (int f = Math.Max(p.Start, 0); f <= p.End && f < n; f++) right[f] = true;
        return Enumerable.Range(0, n).Where(f => left[f] && right[f]).ToList();
    }

    /// <summary>Per-frame head attitude: the rig's own rest neck→head direction carried by
    /// the head bone's world delta from the normalized rest, measured as the signed forward
    /// lean from character up (deg, + = tipping forward = chin down).</summary>
    private static float[] HeadLeans(
        List<XForm[]> frames, SkeletonModel skeleton, int neck, int head,
        IReadOnlyList<XForm> normRest, Vector3 fwd, Vector3 up)
    {
        fwd = Vector3.Normalize(fwd);
        up = Vector3.Normalize(up);
        var restDir = normRest[head].Pos - normRest[neck].Pos;
        var restRotInv = Quaternion.Conjugate(normRest[head].Rot);

        var result = new float[frames.Count];
        for (int f = 0; f < frames.Count; f++)
        {
            var world = WorldRot(frames[f], skeleton, head);
            var dir = Vector3.Normalize(
                Vector3.Transform(restDir, MathQ.Normalize(world * restRotInv)));
            result[f] = MathF.Atan2(Vector3.Dot(dir, fwd), Vector3.Dot(dir, up)) * Rad2Deg;
        }
        return result;
    }

    private static Quaternion WorldRot(XForm[] locals, SkeletonModel skeleton, int bone)
    {
        var rot = locals[bone].Rot;
        for (var b = skeleton[bone].ParentIndex; b >= 0; b = skeleton[b].ParentIndex)
            rot = locals[b].Rot * rot;
        return MathQ.Normalize(rot);
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "humanoid-retargeter.sbproj")))
                return Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root (humanoid-retargeter.sbproj) not found above test directory.");
    }
}
