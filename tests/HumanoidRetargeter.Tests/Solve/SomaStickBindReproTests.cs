using System.Numerics;
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
/// Local-only quality gate against the REAL user-reported repro file in
/// <c>dev/corpus/todo/</c> (not committed; every test silently passes when the file is
/// absent). User report: Neutral_throw_ball_001__A057.bvh (NVIDIA SOMA uniform skeleton,
/// soma_bvh preset at 1.0) — "the orientation is wrong, arms going through [the body],
/// it's just wrong."
/// Root cause: the SOMA bind is bone-length encoding, not a pose — every OFFSET runs along
/// ±X, so identity rest rotations collapse the rig into a stick (measured: character up =
/// world +X, thigh·up = +1.00/−1.00, thighs 180° apart). Canonical frames built on that
/// non-pose produced garbage transfers. The fix: <see cref="RestNormalizer"/> detects the
/// non-anatomical bind and rebuilds the rest from the clip's first frame (an upright
/// N-pose, arms 64–69° below horizontal → <see cref="RestNormalizer.DetectedPose.IPose"/>).
/// </summary>
public class SomaStickBindReproTests
{
    private const string TodoFile = "Neutral_throw_ball_001__A057.bvh";
    private const float Rad2Deg = 180f / MathF.PI;

    [Fact] // skipped (silently green) when the local corpus file is not present
    public void SomaBind_IsNonAnatomical_AndRebuildsFromFirstFrame()
    {
        if (Load() is not var (scene, map, _))
            return;

        Assert.Equal("soma_bvh", map.ProfileName);
        Assert.False(RestNormalizer.IsAnatomicalRest(scene.Skeleton, map, scene.Skeleton.RestWorld));
        Assert.Throws<ArgumentException>(() => RestNormalizer.Normalize(scene.Skeleton, map));

        var (_, report) = RestNormalizer.Normalize(scene.Skeleton, map, scene.Clips[0].Frames[0]);
        Assert.True(report.RebuiltFromReferencePose);
        Assert.Equal(RestNormalizer.DetectedPose.IPose, report.Detected);
        Assert.InRange(report.UpperArmAngleDeg, 60f, 95f);
    }

    /// <summary>The solver must match limb directions in character space — mean per-segment
    /// error ≤ 3° over the clip (the absolute canonical-orientation transfer is exact by
    /// construction once the rest reference is sane; was ~90° garbage on the stick bind).</summary>
    [Fact] // skipped (silently green) when the local corpus file is not present
    public void SomaThrow_LimbDirections_MatchSource_Within3Degrees()
    {
        if (Load() is not var (scene, map, target))
            return;
        var rig = target.Rig;

        var solved = new GeometricSolver().Solve(scene, map, rig, new SolveOptions());

        var srcSkel = scene.Skeleton;
        var tgtSkel = rig.Skeleton;
        var tgtMap = rig.ToMappingResult();
        var (srcNorm, _) = RestNormalizer.Normalize(srcSkel, map, scene.Clips[0].Frames[0]);
        var (tgtNorm, _) = RestNormalizer.Normalize(tgtSkel, tgtMap);
        var srcCanon = CanonicalFrames.Build(srcSkel, map, srcNorm.WorldRest);
        var tgtCanon = CanonicalFrames.Build(tgtSkel, tgtMap, tgtNorm.WorldRest);
        var srcChrInv = Quaternion.Conjugate(
            MathQ.BasisFromForwardUp(srcCanon.CharacterForward, srcCanon.CharacterUp));
        var tgtChrInv = Quaternion.Conjugate(
            MathQ.BasisFromForwardUp(tgtCanon.CharacterForward, tgtCanon.CharacterUp));

        // Strict AbsoluteDirection limb segments (parent role -> chain-child role).
        var segments = new (BoneRole From, BoneRole To)[]
        {
            (BoneRole.UpperArmL, BoneRole.LowerArmL), (BoneRole.LowerArmL, BoneRole.HandL),
            (BoneRole.UpperArmR, BoneRole.LowerArmR), (BoneRole.LowerArmR, BoneRole.HandR),
            (BoneRole.UpperLegL, BoneRole.LowerLegL), (BoneRole.LowerLegL, BoneRole.FootL),
            (BoneRole.UpperLegR, BoneRole.LowerLegR), (BoneRole.LowerLegR, BoneRole.FootR),
        };

        foreach (var (from, to) in segments)
        {
            var sum = 0f;
            var max = 0f;
            for (var f = 0; f < solved.Frames.Count; f++)
            {
                var srcWorld = Fk(srcSkel, scene.Clips[0].Frames[f]);
                var tgtWorld = Fk(tgtSkel, solved.Frames[f]);
                var ds = Vector3.Transform(
                    Dir(srcWorld, map.RoleToBone[from], map.RoleToBone[to]), srcChrInv);
                var dt = Vector3.Transform(
                    Dir(tgtWorld, rig.BoneForRole(from)!.Value, rig.BoneForRole(to)!.Value), tgtChrInv);
                var deg = MathQ.AngleBetween(ds, dt) * Rad2Deg;
                sum += deg;
                max = MathF.Max(max, deg);
            }
            var mean = sum / solved.Frames.Count;
            Assert.True(mean <= 3f, $"{from}->{to}: mean direction error {mean:F2} deg (max {max:F2})");
        }
    }

    /// <summary>On frames where the source wrists are clearly away from the body (scaled
    /// clearance from the hips vertical axis ≥ 25 cm), the converted wrists must keep
    /// real clearance too — the reported failure was arms swinging through the torso.</summary>
    [Fact] // skipped (silently green) when the local corpus file is not present
    public void SomaThrow_Wrists_StayClearOfTheSpineAxis()
    {
        if (Load() is not var (scene, map, target))
            return;
        var rig = target.Rig;

        // Full production pipeline (solver + cleanup passes), as the user runs it.
        var result = Retargeter.Convert(new RetargetRequest
        {
            SourceData = File.ReadAllBytes(Path()),
            SourceFileName = TodoFile,
            MappingOverride = map,
        }, target);
        var clip = result.Clips.First(c => c.Success);
        var frames = clip.SolvedFrames!;

        var srcSkel = scene.Skeleton;
        var tgtSkel = rig.Skeleton;
        var (srcNorm, _) = RestNormalizer.Normalize(srcSkel, map, scene.Clips[0].Frames[0]);
        var (tgtNorm, _) = RestNormalizer.Normalize(tgtSkel, rig.ToMappingResult());
        var srcCanon = CanonicalFrames.Build(srcSkel, map, srcNorm.WorldRest);
        var tgtCanon = CanonicalFrames.Build(tgtSkel, rig.ToMappingResult(), tgtNorm.WorldRest);
        var scale = tgtCanon.HipHeight / srcCanon.HipHeight;

        var srcHips = map.RoleToBone[BoneRole.Hips];
        var tgtHips = rig.BoneForRole(BoneRole.Hips)!.Value;
        var checkedFrames = 0;
        for (var f = 0; f < Math.Min(frames.Count, scene.Clips[0].Frames.Count); f++)
        {
            var srcWorld = Fk(srcSkel, scene.Clips[0].Frames[f]);
            var tgtWorld = Fk(tgtSkel, frames[f]);
            foreach (var hand in new[] { BoneRole.HandL, BoneRole.HandR })
            {
                var srcClear = AxisClearance(
                    srcWorld[map.RoleToBone[hand]].Pos, srcWorld[srcHips].Pos, srcCanon.CharacterUp);
                if (srcClear * scale < 25f)
                    continue; // only judge frames where the source wrist is clearly out
                checkedFrames++;
                var tgtClear = AxisClearance(
                    tgtWorld[rig.BoneForRole(hand)!.Value].Pos, tgtWorld[tgtHips].Pos, tgtCanon.CharacterUp);
                Assert.True(tgtClear >= 0.5f * srcClear * scale,
                    $"frame {f} {hand}: wrist clearance {tgtClear:F1} cm vs scaled source "
                    + $"{srcClear * scale:F1} cm — arm collapsed into the torso");
            }
        }
        Assert.True(checkedFrames >= 20, $"too few clearly-out wrist frames ({checkedFrames})");
    }

    // ---------------------------------------------------------------- helpers

    private static (SourceScene Scene, MappingResult Map, RetargetTargetSpec Target)? Load()
    {
        var path = Path();
        if (!File.Exists(path))
            return null;
        var scene = Retargeter.ImportSource(File.ReadAllBytes(path), TodoFile);
        var (map, _) = Retargeter.ResolveMapping(scene.Skeleton);
        var target = RetargetTargetSpec.SboxDefault(
            File.ReadAllText(RepoFile("Assets", "humanoid_retargeter", "target_rig_sbox.json")));
        return (scene, map, target);
    }

    private static string Path() => RepoFile("dev", "corpus", "todo", TodoFile);

    private static XForm[] Fk(SkeletonModel skel, XForm[] locals)
    {
        var world = new XForm[skel.Count];
        for (var i = 0; i < skel.Count; i++)
        {
            var p = skel[i].ParentIndex;
            world[i] = p < 0 ? locals[i] : XForm.Compose(world[p], locals[i]);
        }
        return world;
    }

    private static Vector3 Dir(XForm[] world, int from, int to)
        => Vector3.Normalize(world[to].Pos - world[from].Pos);

    /// <summary>Perpendicular distance of <paramref name="p"/> from the line through
    /// <paramref name="origin"/> along <paramref name="axis"/>.</summary>
    private static float AxisClearance(Vector3 p, Vector3 origin, Vector3 axis)
    {
        var v = p - origin;
        return (v - axis * Vector3.Dot(v, axis)).Length();
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "humanoid-retargeter.sbproj")))
                return System.IO.Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root (humanoid-retargeter.sbproj) not found above test directory.");
    }
}
