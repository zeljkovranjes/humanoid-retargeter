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
/// Local-only planted-foot quality gates against the REAL user-reported repro files in
/// <c>dev/corpus/todo/</c> (not committed; every test silently passes when a file is
/// absent). User reports: 47_01.bvh "feet are a little bent upward", Defenses.fbx "feet
/// seem to be slightly bent inward, like part of the bone sticks out" — both were the
/// rest foot-direction divergence (23–44° vs the s&amp;box ankle) leaking through absolute
/// direction matching. The fix: feet transfer as
/// <see cref="RoleTransferMode.CharacterDeltaFromRest"/>, plus the
/// <see cref="FootGroundAlign"/> stance recalibration when the source's normalized rest is
/// not a plausible stance (Defenses: T-pose normalization swings its bent-leg rest feet
/// 14–26° off the rig's true stance).
/// </summary>
public class GroundedFeetReproTests
{
    private const float Rad2Deg = 180f / MathF.PI;

    // ---------------------------------------------------------------- gates

    /// <summary>CMU-style rig whose rest IS a stance: the full pipeline must track the
    /// source's planted foot pitch (deviation from each rig's own rest anatomy) — the old
    /// constant ~23–25° "bent upward" leak is gone, and the stance recalibration must stay
    /// dormant (the planted offset is genuine articulation).</summary>
    [Fact] // skipped (silently green) when the local corpus file is not present
    public void Cmu4701_PlantedFootPitch_TracksSource_Within3Degrees()
    {
        if (Load("47_01.bvh") is not { } data)
            return;

        foreach (var side in Sides(data))
        {
            Assert.True(side.PlantedFrames >= 30, $"{side.Name}: too few planted frames");
            Assert.True(MathF.Abs(side.MeanPitchErrVsSource) <= 3f,
                $"{side.Name}: planted pitch error vs source {side.MeanPitchErrVsSource:F2} deg");
            Assert.True(side.MaxAbsPitchErrVsSource <= 5f,
                $"{side.Name}: planted pitch error max {side.MaxAbsPitchErrVsSource:F2} deg");
        }
    }

    /// <summary>ARP-style rig whose (normalized) rest is NOT a stance: the recalibration
    /// must level the planted soles against the ground — mean planted pitch deviation from
    /// the target's own flat-stance rest ≈ 0 (was +15…+17° "bent inward/upward" before, and
    /// would be −12…−25° toe-through-floor under a plain rest-relative replay).</summary>
    [Fact] // skipped (silently green) when the local corpus file is not present
    public void Defenses_PlantedFeet_LevelWithGround_Within3Degrees()
    {
        if (Load("Defenses.fbx") is not { } data)
            return;

        foreach (var side in Sides(data))
        {
            Assert.True(side.PlantedFrames >= 100, $"{side.Name}: too few planted frames");
            Assert.True(MathF.Abs(side.MeanTargetPitchDelta) <= 3f,
                $"{side.Name}: planted feet not level with ground — mean pitch delta "
                + $"{side.MeanTargetPitchDelta:F2} deg from the target's flat-stance rest");
        }
    }

    // ---------------------------------------------------------------- pipeline + metrics

    private sealed record SideStats(
        string Name, int PlantedFrames, float MeanTargetPitchDelta,
        float MeanPitchErrVsSource, float MaxAbsPitchErrVsSource);

    private sealed record ReproData(
        SourceScene Scene, MappingResult Map, TargetRig Rig, List<XForm[]> TargetFrames);

    private static ReproData? Load(string todoFileName)
    {
        var path = RepoFile("dev", "corpus", "todo", todoFileName);
        if (!File.Exists(path))
            return null;

        var bytes = File.ReadAllBytes(path);
        var scene = Retargeter.ImportSource(bytes, todoFileName);
        var (map, _) = Retargeter.ResolveMapping(scene.Skeleton);
        var target = RetargetTargetSpec.SboxDefault(
            File.ReadAllText(RepoFile("Assets", "data", "humanoid_retargeter", "target_rig_sbox.json")));

        var result = Retargeter.Convert(new RetargetRequest
        {
            SourceData = bytes,
            SourceFileName = todoFileName,
            MappingOverride = map,
        }, target);
        var clip = result.Clips.First(c => c.Success);
        return new ReproData(scene, map, target.Rig, clip.SolvedFrames!);
    }

    private static IEnumerable<SideStats> Sides(ReproData data)
    {
        var (scene, map, rig, tgtFrames) = (data.Scene, data.Map, data.Rig, data.TargetFrames);
        var srcSkel = scene.Skeleton;
        var tgtSkel = rig.Skeleton;
        var tgtMap = rig.ToMappingResult();
        var (srcNorm, _) = RestNormalizer.Normalize(srcSkel, map);
        var (tgtNorm, _) = RestNormalizer.Normalize(tgtSkel, tgtMap);
        var srcUp = Vector3.Normalize(CanonicalFrames.Build(srcSkel, map, srcNorm.WorldRest).CharacterUp);
        var tgtUp = Vector3.Normalize(CanonicalFrames.Build(tgtSkel, tgtMap, tgtNorm.WorldRest).CharacterUp);

        FootChain Chain(Func<BoneRole, int?> bone, BoneRole u, BoneRole l, BoneRole f, BoneRole t) => new()
        {
            Hip = bone(u)!.Value,
            Knee = bone(l)!.Value,
            Ankle = bone(f)!.Value,
            Toe = bone(t),
        };
        int? SrcBone(BoneRole r) => map.RoleToBone.TryGetValue(r, out var b) ? b : null;

        var srcL = Chain(SrcBone, BoneRole.UpperLegL, BoneRole.LowerLegL, BoneRole.FootL, BoneRole.ToeL);
        var srcR = Chain(SrcBone, BoneRole.UpperLegR, BoneRole.LowerLegR, BoneRole.FootR, BoneRole.ToeR);
        var clip = scene.Clips[0];
        var (plantsL, plantsR) = FootPlant.DetectPlantIntervals(
            clip.Frames, srcSkel, srcL, srcR, srcUp, clip.Fps);

        foreach (var (name, footRole, toeRole, plants) in new[]
                 {
                     ("LEFT", BoneRole.FootL, BoneRole.ToeL, plantsL),
                     ("RIGHT", BoneRole.FootR, BoneRole.ToeR, plantsR),
                 })
        {
            var srcPitch = PitchDeltas(
                clip.Frames, srcSkel, map.RoleToBone[footRole], map.RoleToBone[toeRole],
                srcNorm.WorldRest, srcUp);
            var tgtPitch = PitchDeltas(
                tgtFrames, tgtSkel, rig.BoneForRole(footRole)!.Value, rig.BoneForRole(toeRole)!.Value,
                tgtNorm.WorldRest, tgtUp);

            var planted = new List<int>();
            foreach (var p in plants)
            {
                for (int f = Math.Max(p.Start, 0); f <= p.End && f < tgtFrames.Count; f++)
                    planted.Add(f);
            }

            if (planted.Count == 0)
            {
                yield return new SideStats(name, 0, 0f, 0f, 0f);
                continue;
            }

            var meanTgt = planted.Average(f => tgtPitch[f]);
            var meanErr = planted.Average(f => tgtPitch[f] - srcPitch[f]);
            var maxErr = planted.Max(f => MathF.Abs(tgtPitch[f] - srcPitch[f]));
            yield return new SideStats(name, planted.Count, meanTgt, meanErr, maxErr);
        }
    }

    /// <summary>Per-frame foot pitch (vs the ground plane) as a delta from the rig's own
    /// rest anatomy: rest foot→toe direction carried by the foot's world delta from the
    /// normalized rest.</summary>
    private static float[] PitchDeltas(
        List<XForm[]> frames, SkeletonModel skeleton, int foot, int toe,
        IReadOnlyList<XForm> normRest, Vector3 up)
    {
        var restDir = normRest[toe].Pos - normRest[foot].Pos;
        float Pitch(Vector3 d) =>
            MathF.Asin(Math.Clamp(Vector3.Dot(Vector3.Normalize(d), up), -1f, 1f)) * Rad2Deg;
        var restPitch = Pitch(restDir);
        var restRotInv = Quaternion.Conjugate(normRest[foot].Rot);

        var result = new float[frames.Count];
        for (int f = 0; f < frames.Count; f++)
        {
            var world = FootWorldRot(frames[f], skeleton, foot);
            var dir = Vector3.Transform(restDir, MathQ.Normalize(world * restRotInv));
            result[f] = Pitch(dir) - restPitch;
        }
        return result;
    }

    private static Quaternion FootWorldRot(XForm[] locals, SkeletonModel skeleton, int bone)
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
