using System.Globalization;
using System.Numerics;
using System.Text;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Solve;
using HumanoidRetargeter.Core.Target;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;
using HumanoidRetargeter.Core;

namespace HumanoidRetargeter.Tests.Solve;

/// <summary>
/// Clip placement normalization for sources without an authored rest placement
/// (<see cref="SourceScene.RestPlacementAuthored"/> = false, i.e. BVH). User report:
/// Armchair1.bvh — "the model is in the air … like it's skywalking … and it's outside of
/// the modeldoc grid". A BVH rest skeleton is OFFSETs only (root at the file origin, ground
/// at the rest feet) while its motion lives in absolute capture-volume coordinates (ground
/// at the capture floor, subject anywhere on the stage): Armchair1 starts at (347, 107, 86)
/// against a rest whose ground sits at −107, so the solved pelvis hovered a full hip height
/// above the s&amp;box rig, meters off origin. The solver must absorb the per-clip constant —
/// frame-0 horizontal offset and motion-ground↔rest-ground gap — into the pelvis-travel
/// reference, while preserving all WITHIN-clip motion and leaving authored-placement
/// (FBX/glTF) sources untouched.
/// </summary>
public class ClipPlacementTests
{
    // ---------------------------------------------------------------- fixture

    /// <summary>
    /// Mixamo-named walk BVH (WalkFixture's rig: rest hip height 85, rest ground −93)
    /// whose hips ride at world (300 + f·drift, 95, 80): far off origin like a real
    /// capture-volume take, feet planted at world Y ≈ +2. Legs swing like WalkFixture so
    /// plant detection has something to chew on.
    /// </summary>
    private static byte[] OffsetWalkBvh(float driftPerFrame = 0f, int frames = 60)
    {
        var text = WalkFixture.SyntheticWalkBvh();
        // Re-write the motion rows' root position channels: X = 300 + f·drift (replacing
        // the ±1.5 cm sway), Z = 80. Rows are the lines after "Frame Time:".
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        var inMotion = false;
        var f = 0;
        foreach (var line in lines)
        {
            if (!inMotion)
            {
                sb.Append(line).Append('\n');
                inMotion = line.StartsWith("Frame Time:", StringComparison.Ordinal);
                continue;
            }
            if (string.IsNullOrWhiteSpace(line) || f >= frames)
                continue;
            var cols = line.Split(' ');
            cols[0] = (300f + f * driftPerFrame).ToString("0.######", CultureInfo.InvariantCulture);
            cols[2] = 80f.ToString("0.######", CultureInfo.InvariantCulture);
            sb.Append(string.Join(' ', cols)).Append('\n');
            f++;
        }
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static TargetRig Rig()
        => RetargetTargetSpec.SboxDefault(File.ReadAllText(
            TestUtil.RepoFile("Assets", "humanoid_retargeter", "target_rig_sbox.json"))).Rig;

    private static List<XForm[]> Solve(SourceScene scene, TargetRig rig)
    {
        var (map, _) = Retargeter.ResolveMapping(scene.Skeleton);
        return new GeometricSolver()
            .Solve(scene, map, rig, new SolveOptions())
            .Frames;
    }

    private static Vector3 PelvisWorld(TargetRig rig, XForm[] frame)
    {
        var pelvis = rig.BoneForRole(BoneRole.Hips)!.Value;
        var world = new Pose(frame).ToWorld(rig.Skeleton);
        return world[pelvis].Pos;
    }

    /// <summary>Horizontal (ground-plane) distance in the target's Y-up cm space.</summary>
    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        var d = a - b;
        return MathF.Sqrt(d.X * d.X + d.Z * d.Z);
    }

    // ---------------------------------------------------------------- gates

    [Fact]
    public void BvhFarFromOrigin_Solves_StartingOverTargetOrigin()
    {
        var scene = Retargeter.ImportSource(OffsetWalkBvh(), "offset_walk.bvh");
        Assert.False(scene.RestPlacementAuthored); // BVH: placement normalization engages
        var rig = Rig();
        var frames = Solve(scene, rig);

        var restPelvis = rig.Skeleton.RestWorld[rig.BoneForRole(BoneRole.Hips)!.Value].Pos;
        var solved = PelvisWorld(rig, frames[0]);
        Assert.True(HorizontalDistance(solved, restPelvis) < 5f,
            $"solved pelvis starts {HorizontalDistance(solved, restPelvis):F1} cm off the "
            + $"target origin (at {solved}) — capture-volume offset not normalized");
    }

    [Fact]
    public void BvhWithMotionGroundAboveRestGround_Solves_Grounded()
    {
        var scene = Retargeter.ImportSource(OffsetWalkBvh(), "offset_walk.bvh");
        var rig = Rig();
        var frames = Solve(scene, rig);

        // Planted-foot height must track the target's own rest ankle height (the fixture
        // plants each foot at source rest height for most of the clip), not hover a hip
        // height above it.
        var ankle = rig.BoneForRole(BoneRole.FootL)!.Value;
        var restAnkleY = rig.Skeleton.RestWorld[ankle].Pos.Y;
        var minAnkleY = float.MaxValue;
        foreach (var frame in frames)
        {
            var world = new Pose(frame).ToWorld(rig.Skeleton);
            minAnkleY = MathF.Min(minAnkleY, world[ankle].Pos.Y);
        }
        Assert.True(MathF.Abs(minAnkleY - restAnkleY) < 10f,
            $"lowest solved ankle at {minAnkleY:F1} cm vs rest ankle {restAnkleY:F1} cm — "
            + "clip not grounded");
    }

    /// <summary>Only the constant placement is normalized: a clip that walks away from its
    /// start must still walk the same (scaled) distance.</summary>
    [Fact]
    public void WithinClipTravel_IsPreserved()
    {
        const float drift = 2f; // cm per frame of source-space forward drift
        var scene = Retargeter.ImportSource(OffsetWalkBvh(drift), "drift_walk.bvh");
        var rig = Rig();
        var frames = Solve(scene, rig);

        // Expected target-space travel = source travel × hip-height ratio. Source rest hip
        // height (WalkFixture rig): hips 93 above the toe bottom (5+40+40+8); measure the
        // target's from its rig to stay robust to rig edits.
        var (map, _) = Retargeter.ResolveMapping(scene.Skeleton);
        var (srcNorm, _) = RestNormalizer.Normalize(scene.Skeleton, map);
        var srcHip = CanonicalFrames.Build(scene.Skeleton, map, srcNorm.WorldRest).HipHeight;
        var tgtMap = rig.ToMappingResult();
        var (tgtNorm, _) = RestNormalizer.Normalize(rig.Skeleton, tgtMap);
        var tgtHip = CanonicalFrames.Build(rig.Skeleton, tgtMap, tgtNorm.WorldRest).HipHeight;

        var sourceTravel = drift * (frames.Count - 1);
        var expected = sourceTravel * (tgtHip / srcHip);
        var actual = HorizontalDistance(
            PelvisWorld(rig, frames[^1]), PelvisWorld(rig, frames[0]));
        Assert.True(MathF.Abs(actual - expected) < 0.15f * expected + 3f,
            $"clip travel {actual:F1} cm vs expected {expected:F1} cm — placement "
            + "normalization must not eat within-clip motion");
    }

    /// <summary>Sources with an authored rest placement (FBX/glTF — modeled here by
    /// re-wrapping the same data with the default flag) keep absolute root translations:
    /// normalization is strictly opt-in per format.</summary>
    [Fact]
    public void AuthoredPlacementSource_IsNotRecentered()
    {
        var imported = Retargeter.ImportSource(OffsetWalkBvh(), "offset_walk.bvh");
        var authored = new SourceScene(
            imported.Skeleton, imported.Clips, imported.UnitScaleCm,
            imported.UpAxis, imported.UpAxisSign,
            imported.FrontAxis, imported.FrontAxisSign,
            imported.CoordAxis, imported.CoordAxisSign,
            imported.OriginalUpAxis); // RestPlacementAuthored defaults to true
        Assert.True(authored.RestPlacementAuthored);

        var rig = Rig();
        var frames = Solve(authored, rig);
        var restPelvis = rig.Skeleton.RestWorld[rig.BoneForRole(BoneRole.Hips)!.Value].Pos;
        var solved = PelvisWorld(rig, frames[0]);
        Assert.True(HorizontalDistance(solved, restPelvis) > 50f,
            "authored-placement source was recentered — the flag must gate normalization");
    }
}
