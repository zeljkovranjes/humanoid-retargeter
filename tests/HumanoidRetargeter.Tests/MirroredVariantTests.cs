using System.Numerics;
using System.Text;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Solve;
using HumanoidRetargeter.Core.Target;
using Xunit;
using HumanoidRetargeter.Core;

namespace HumanoidRetargeter.Tests;

/// <summary>
/// Facade tests for <see cref="RetargetRequest.CreateMirroredVariant"/> plus direct
/// <see cref="ClipMirror"/> math proofs (double-mirror involution, sagittal-plane geometry).
/// </summary>
public class MirroredVariantTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "humanoid-retargeter.sbproj")))
                return Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            dir = dir.Parent!;
        }
        throw new InvalidOperationException("Repo root (humanoid-retargeter.sbproj) not found.");
    }

    private static readonly Lazy<RetargetTargetSpec> SboxTarget = new(()
        => RetargetTargetSpec.SboxDefault(
            File.ReadAllText(RepoFile("Assets", "data", "humanoid_retargeter", "target_rig_sbox.json"))));

    private static RetargetRequest WalkRequest(bool mirrored = true, bool footsteps = false) => new()
    {
        SourceData = Encoding.UTF8.GetBytes(WalkFixture.SyntheticWalkBvh()),
        SourceFileName = "synth_walk.bvh",
        CreateMirroredVariant = mirrored,
        GenerateFootstepEvents = footsteps,
    };

    // ---------------------------------------------------------------- math: involution

    [Fact]
    public void ClipMirror_DoubleMirror_IsBitExactIdentity()
    {
        // The s&box rig's computed lateral axis snaps to an exact coordinate axis, making
        // every reflection a pure IEEE sign flip — so mirror ∘ mirror must reproduce the
        // input BIT-EXACTLY (component equality, not tolerance).
        var result = Retargeter.Convert(WalkRequest(mirrored: false), SboxTarget.Value);
        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);
        var frames = clip.SolvedFrames!;

        var rig = SboxTarget.Value.Rig;
        var twice = ClipMirror.Mirror(ClipMirror.Mirror(frames, rig), rig);

        Assert.Equal(frames.Count, twice.Count);
        for (var f = 0; f < frames.Count; f++)
        {
            for (var b = 0; b < frames[f].Length; b++)
            {
                Assert.True(frames[f][b] == twice[f][b],
                    $"frame {f} bone {b} ('{rig.Skeleton[b].Name}'): " +
                    $"{frames[f][b]} != {twice[f][b]} (double mirror must be bit-exact)");
            }
        }

        // And the single mirror is NOT the identity (it actually does something).
        var once = ClipMirror.Mirror(frames, rig);
        Assert.Contains(Enumerable.Range(0, frames.Count),
            f => Enumerable.Range(0, frames[f].Length).Any(b => frames[f][b] != once[f][b]));
    }

    // ---------------------------------------------------------------- plant-phase swap

    [Fact]
    public void Convert_MirroredVariant_SwapsFootPlantPhases()
    {
        // The synthetic walk has ONE left touchdown (~frame 15) and ONE right touchdown
        // (~frame 45). Plant detection on the mirrored clip must see them swapped: the
        // mirrored LEFT foot touches down where the primary RIGHT did, and vice versa.
        var result = Retargeter.Convert(WalkRequest(footsteps: true), SboxTarget.Value);
        Assert.Equal(2, result.Clips.Count);
        Assert.All(result.Clips, c => Assert.True(c.Success, c.Error));

        var primary = result.Clips.Single(c => !c.IsMirroredVariant);
        var mirrored = result.Clips.Single(c => c.IsMirroredVariant);
        Assert.Equal(primary.ClipName + "_M", mirrored.ClipName);

        int[] Feet(ClipResult clip, string foot)
            => clip.FootstepEvents.Where(e => e.Foot == foot).Select(e => e.Frame).ToArray();

        // Mirroring is an exact reflection of the solved frames, so the swapped plant
        // phases land on exactly the same frames.
        Assert.Equal(Feet(primary, "1"), Feet(mirrored, "0")); // primary right → mirrored left
        Assert.Equal(Feet(primary, "0"), Feet(mirrored, "1")); // primary left → mirrored right

        // Sanity: the phases really are distinct (left ~15, right ~45).
        Assert.NotEqual(Feet(primary, "0"), Feet(primary, "1"));
    }

    // ---------------------------------------------------------------- sagittal geometry

    [Fact]
    public void Convert_MirroredVariant_PelvisLateralOffsetNegates()
    {
        var result = Retargeter.Convert(WalkRequest(), SboxTarget.Value);
        var primary = result.Clips.Single(c => !c.IsMirroredVariant);
        var mirrored = result.Clips.Single(c => c.IsMirroredVariant);
        Assert.True(primary.Success, primary.Error);
        Assert.True(mirrored.Success, mirrored.Error);

        var rig = SboxTarget.Value.Rig;
        var pelvis = rig.BoneForRole(BoneRole.Hips)!.Value;
        var lateral = RestLateral(rig);

        var maxAbs = 0f;
        for (var f = 0; f < primary.SolvedFrames!.Count; f++)
        {
            var p = Vector3.Dot(new Pose(primary.SolvedFrames[f]).ToWorld(rig.Skeleton)[pelvis].Pos, lateral);
            var m = Vector3.Dot(new Pose(mirrored.SolvedFrames![f]).ToWorld(rig.Skeleton)[pelvis].Pos, lateral);
            Assert.True(MathF.Abs(p + m) < 1e-3f,
                $"frame {f}: pelvis lateral {p:F4} should negate to {-p:F4}, got {m:F4}");
            maxAbs = MathF.Max(maxAbs, MathF.Abs(p));
        }

        // The fixture's hip sway makes this non-trivial: the lateral offset is really moving.
        Assert.True(maxAbs > 0.5f, $"expected ≥0.5 cm of lateral sway, got {maxAbs:F3} cm");
    }

    [Fact]
    public void Convert_MirroredVariant_FootTrajectoriesSwapSides()
    {
        var result = Retargeter.Convert(WalkRequest(), SboxTarget.Value);
        var primary = result.Clips.Single(c => !c.IsMirroredVariant);
        var mirrored = result.Clips.Single(c => c.IsMirroredVariant);

        var rig = SboxTarget.Value.Rig;
        var footL = rig.BoneForRole(BoneRole.FootL)!.Value;
        var footR = rig.BoneForRole(BoneRole.FootR)!.Value;
        var lateral = RestLateral(rig);

        // The mirrored clip's LEFT ankle must follow the primary RIGHT ankle's path
        // reflected across the sagittal plane: same height/forward, negated lateral.
        // Tolerance: the twin is a full re-solve of the mirrored SOURCE (G8 source-side
        // mirror), reflection-faithful to ~2e-3 cm on the symmetric fixture rather than
        // bit-exact; 0.01 cm still asserts a physically perfect mirror.
        for (var f = 0; f < primary.SolvedFrames!.Count; f += 7)
        {
            var pr = new Pose(primary.SolvedFrames[f]).ToWorld(rig.Skeleton)[footR].Pos;
            var ml = new Pose(mirrored.SolvedFrames![f]).ToWorld(rig.Skeleton)[footL].Pos;
            var reflected = pr - 2f * Vector3.Dot(pr, lateral) * lateral;
            TestUtil.AssertVectorEqual(reflected, ml, 1e-2f);
        }
    }

    // ---------------------------------------------------------------- source-mirrored twin
    //
    // G8 regeneration mirror fix (southpaw project, gate3_review.md 3.4 plus the G8
    // bisect evidence). The OLD test here ("IkBonesFollowMirroredBody") asserted a bare
    // target-space re-bake. Target-space channel mirroring in ANY convention produced
    // data the engine's render-side sequence evaluation mangles (a mirrored pelvis
    // channel alone renders the model upside down while every CPU bone API reports a
    // perfect mirror), so the twin is now produced by mirroring the SOURCE clip and
    // running the COMPLETE primary pipeline on it. The contract: the twin is a
    // first-class primary clip (helper construction, aim constants, channel set all
    // primary-identical) whose solved body is the reflection of the primary's within
    // solver tolerance.

    [Fact]
    public void Convert_MirroredVariant_HelperConstructionMatchesPrimary()
    {
        // The twin's helpers must be a FIXED POINT of the primary helper chain: applying
        // IkBoneBaker.Bake + the orphan-hips re-anchor to the twin's own solved frames
        // must change nothing. That proves the twin ends in exactly the state the
        // primary pipeline produces for its clips (bake, then re-anchor), i.e. its data
        // shape is primary-identical and the engine cannot tell it apart.
        var result = Retargeter.Convert(WalkRequest(), SboxTarget.Value);
        var mirrored = result.Clips.Single(c => c.IsMirroredVariant);
        Assert.True(mirrored.Success, mirrored.Error);

        var rig = SboxTarget.Value.Rig;
        var skeleton = rig.Skeleton;

        var reapplied = mirrored.SolvedFrames!
            .Select(f => (HumanoidRetargeter.Core.Maths.XForm[])f.Clone())
            .ToList();
        IkBoneBaker.Bake(reapplied, rig);
        Retargeter.TestHook_FollowOrphans(rig, reapplied);

        for (var f = 0; f < reapplied.Count; f += 5)
        {
            for (var b = 0; b < skeleton.Count; b++)
            {
                TestUtil.AssertVectorEqual(
                    mirrored.SolvedFrames[f][b].Pos, reapplied[f][b].Pos, 1e-3f);
            }
        }
    }

    [Fact]
    public void Convert_MirroredVariant_AimReferencesStayAtRestLikeEveryPrimaryClip()
    {
        // The aim-space reference bones (aim_matrix_*) and the hold/attach helpers are
        // engine-expected CONSTANTS: every shipped citizen clip holds them at REST
        // locals and the model's aim/eye constraint setup is authored against exactly
        // those. The twin must carry the identical convention.
        var result = Retargeter.Convert(WalkRequest(), SboxTarget.Value);
        var mirrored = result.Clips.Single(c => c.IsMirroredVariant);
        Assert.True(mirrored.Success, mirrored.Error);

        var rig = SboxTarget.Value.Rig;
        var skeleton = rig.Skeleton;
        var pinnedChecked = 0;
        for (var f = 0; f < mirrored.SolvedFrames!.Count; f += 7)
        {
            for (var i = 0; i < skeleton.Count; i++)
            {
                var name = skeleton[i].Name;
                if (!name.StartsWith("aim_matrix", StringComparison.Ordinal)
                    && !name.EndsWith("_IK_attach", StringComparison.Ordinal)
                    && name is not ("hold_L" or "hold_R"))
                    continue;
                TestUtil.AssertVectorEqual(
                    skeleton[i].RestLocal.Pos, mirrored.SolvedFrames[f][i].Pos, 1e-3f);
                pinnedChecked++;
            }
        }
        Assert.True(pinnedChecked > 0, "no aim-space reference bones found on the sbox rig");
    }

    // ---------------------------------------------------------------- asymmetric-rest exclusions

    [Fact]
    public void MirrorSafeExclusions_KeepsSymmetric_LiftsAsymmetricRestBones()
    {
        // G8 mirror fix MECHANISM 1: channel-excluded constraint-driven bones whose L/R
        // rest locals are NOT mirror conjugates (the citizen *_twist1 chains and
        // neck_clothing, measured 19 to 37 cm off in the Gate 3 review) must get explicit
        // channels on mirrored clips; truly symmetric helpers stay excluded as on primary
        // clips.
        var citizen = RetargetTargetSpec.SboxCitizen(File.ReadAllText(
            TestUtil.RepoFile("Assets", "data", "humanoid_retargeter", "target_rig_sbox_citizen.json")));
        var rig = citizen.Rig;
        var excluded = new HashSet<int>(rig.BonesOfClass(BoneClass.ConstraintDriven));
        var safe = ClipMirror.MirrorSafeExclusions(rig, excluded);
        Assert.NotNull(safe);

        int IndexOf(string n) => rig.Skeleton.IndexOf(n);

        foreach (var name in new[] { "arm_upper_L_twist0", "arm_upper_R_twist0",
            "leg_upper_L_twist0", "leg_upper_R_twist0" })
        {
            var i = IndexOf(name);
            if (i >= 0 && excluded.Contains(i))
                Assert.Contains(i, safe!);
        }

        foreach (var name in new[] { "leg_upper_L_twist1", "leg_upper_R_twist1",
            "leg_lower_L_twist1", "leg_lower_R_twist1",
            "arm_upper_L_twist1", "arm_upper_R_twist1",
            "arm_lower_L_twist1", "arm_lower_R_twist1", "neck_clothing" })
        {
            var i = IndexOf(name);
            if (i >= 0 && excluded.Contains(i))
                Assert.DoesNotContain(i, safe!);
        }
    }

    [Fact]
    public void ClipMirror_HierarchyInconsistentHelpers_FkSolvedToExactReflection()
    {
        // ClipMirror utility contract (kept for measurement tooling): the citizen rig
        // parents arm_elbow_helper_R under arm_lower_R_twist0 while arm_elbow_helper_L
        // hangs under arm_lower_L (the W3a-documented quirk), so the partner's
        // conjugated LOCAL cannot place them; ClipMirror FK-solves their locals so the
        // mirrored WORLD is the exact sagittal reflection of the partner's world.
        var citizen = RetargetTargetSpec.SboxCitizen(File.ReadAllText(
            TestUtil.RepoFile("Assets", "data", "humanoid_retargeter", "target_rig_sbox_citizen.json")));
        var result = Retargeter.Convert(WalkRequest(mirrored: false), citizen);
        var primary = result.Clips.Single();
        Assert.True(primary.Success, primary.Error);

        var rig = citizen.Rig;
        var skeleton = rig.Skeleton;
        var lateral = RestLateral(rig);
        var mirroredFrames = ClipMirror.Mirror(primary.SolvedFrames!, rig);

        var checkedHelpers = 0;
        foreach (var (name, partner) in new[]
        {
            ("arm_elbow_helper_L", "arm_elbow_helper_R"),
            ("arm_elbow_helper_R", "arm_elbow_helper_L"),
            ("leg_knee_helper_L", "leg_knee_helper_R"),
            ("leg_knee_helper_R", "leg_knee_helper_L"),
        })
        {
            var i = skeleton.IndexOf(name);
            var p = skeleton.IndexOf(partner);
            if (i < 0 || p < 0)
                continue;
            checkedHelpers++;
            for (var f = 0; f < primary.SolvedFrames!.Count; f += 9)
            {
                var wp = new Pose(primary.SolvedFrames[f]).ToWorld(skeleton);
                var wm = new Pose(mirroredFrames[f]).ToWorld(skeleton);
                var src = wp[p].Pos;
                var reflected = src - 2f * Vector3.Dot(src, lateral) * lateral;
                TestUtil.AssertVectorEqual(reflected, wm[i].Pos, 0.05f);
            }
        }
        Assert.True(checkedHelpers > 0, "citizen rig should carry the elbow/knee helpers");
    }

    [Fact]
    public void Convert_MirroredVariant_ChannelSetMatchesPrimaryExactly()
    {
        // DMX data-shape parity (southpaw G8 mirror fix): the mirrored clip carries the
        // IDENTICAL channel set as the primary: constraint-driven bones stay excluded on
        // both (the engine's AnimConstraintList drives them for any pose, mirrored or
        // not); a twin with a different channel set is a data shape no primary clip has.
        var citizen = RetargetTargetSpec.SboxCitizen(File.ReadAllText(
            TestUtil.RepoFile("Assets", "data", "humanoid_retargeter", "target_rig_sbox_citizen.json")));
        var result = Retargeter.Convert(WalkRequest(), citizen);
        var primary = result.Clips.Single(c => !c.IsMirroredVariant);
        var mirrored = result.Clips.Single(c => c.IsMirroredVariant);
        Assert.True(primary.Success, primary.Error);
        Assert.True(mirrored.Success, mirrored.Error);

        foreach (var bone in citizen.Rig.Skeleton.Bones)
        {
            var needle = $"\"{bone.Name}_o\"";
            Assert.Equal(
                primary.DmxContent!.Contains(needle, StringComparison.Ordinal),
                mirrored.DmxContent!.Contains(needle, StringComparison.Ordinal));
        }
    }

    // ---------------------------------------------------------------- naming / defaults

    [Fact]
    public void ConvertBatch_MirroredNames_CollisionSuffixAsUsual()
    {
        var result = Retargeter.ConvertBatch(
            new[] { WalkRequest(), WalkRequest() }, SboxTarget.Value);

        Assert.Equal(4, result.Clips.Count);
        Assert.All(result.Clips, c => Assert.True(c.Success, c.Error));
        Assert.Equal(
            new[] { "synth_walk", "synth_walk_M", "synth_walk_2", "synth_walk_2_M" },
            result.Clips.Select(c => c.ClipName));
        Assert.Equal(new[] { false, true, false, true },
            result.Clips.Select(c => c.IsMirroredVariant));

        // All four registered in the standalone vmdl with distinct DMX files.
        Assert.Equal(4, result.Clips.Select(c => c.DmxFileName).Distinct().Count());
        foreach (var clip in result.Clips)
            Assert.Contains($"name = \"{clip.ClipName}\"", result.StandaloneVmdl);
    }

    [Fact]
    public void Convert_DefaultOff_ProducesNoMirroredVariant()
    {
        var result = Retargeter.Convert(WalkRequest(mirrored: false), SboxTarget.Value);
        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);
        Assert.False(clip.IsMirroredVariant);
        Assert.DoesNotContain("_M", clip.ClipName);
    }

    [Fact]
    public void Convert_MirroredVariant_PreservesLoopingAndFps()
    {
        var result = Retargeter.Convert(new RetargetRequest
        {
            SourceData = Encoding.UTF8.GetBytes(WalkFixture.SyntheticWalkBvh()),
            SourceFileName = "synth_walk.bvh",
            CreateMirroredVariant = true,
            LoopingOverride = true,
        }, SboxTarget.Value);

        var primary = result.Clips.Single(c => !c.IsMirroredVariant);
        var mirrored = result.Clips.Single(c => c.IsMirroredVariant);
        Assert.True(mirrored.Looping);
        Assert.Equal(primary.Fps, mirrored.Fps);
        Assert.Equal(primary.SolvedFrames!.Count, mirrored.SolvedFrames!.Count);
        Assert.False(string.IsNullOrEmpty(mirrored.DmxContent));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Target lateral (left-positive) from public rest geometry, orthogonalized
    /// against the character up and snapped to the dominant coordinate axis — mirrors what
    /// ClipMirror computes internally (the snap keeps assertion tolerances meaningful).</summary>
    private static Vector3 RestLateral(TargetRig rig)
    {
        Vector3 Pos(BoneRole role) => rig.Skeleton.RestWorld[rig.BoneForRole(role)!.Value].Pos;
        var midHips = (Pos(BoneRole.UpperLegL) + Pos(BoneRole.UpperLegR)) * 0.5f;
        var midShoulders = (Pos(BoneRole.UpperArmL) + Pos(BoneRole.UpperArmR)) * 0.5f;
        var up = Vector3.Normalize(midShoulders - midHips);
        var across = Pos(BoneRole.UpperLegL) - Pos(BoneRole.UpperLegR);
        var lateral = Vector3.Normalize(across - up * Vector3.Dot(across, up));

        var a = Vector3.Abs(lateral);
        if (a.Y <= 1e-3f && a.Z <= 1e-3f)
            return Vector3.UnitX;
        if (a.X <= 1e-3f && a.Z <= 1e-3f)
            return Vector3.UnitY;
        if (a.X <= 1e-3f && a.Y <= 1e-3f)
            return Vector3.UnitZ;
        return lateral;
    }
}
