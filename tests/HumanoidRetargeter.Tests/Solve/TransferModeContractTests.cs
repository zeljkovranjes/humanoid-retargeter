using System.Numerics;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Solve;
using Xunit;
using Skel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Solve;

/// <summary>
/// The <see cref="SolveOptions.TransferModes"/> contract: null (default) =
/// <see cref="SolveOptions.DefaultTransferModes"/> plus the solver's fallback heuristics
/// (virtual-foot delta, posed-rest-head absolute gaze); a NON-NULL map is exact — the
/// caller's entries win, roles absent from the map are
/// <see cref="RoleTransferMode.AbsoluteDirection"/>, and every fallback heuristic
/// is disabled. An empty map is therefore the fully absolute legacy behavior (the editor's
/// checkbox-off path). Shares fixtures/helpers with <see cref="GeometricSolverTests"/>.
/// </summary>
public class TransferModeContractTests
{
    /// <summary>
    /// A toe-less source's foot direction is a virtual character-forward extension, so under
    /// the DEFAULT (null) modes the solver falls back to delta transfer for the feet. An
    /// explicit map containing Foot L/R = <see cref="RoleTransferMode.AbsoluteDirection"/> is
    /// a contract: the fallback must NOT override it — the feet solve absolute even though
    /// the source primary is virtual — while every other role matches the defaults run
    /// (the explicit map reproduces <see cref="SolveOptions.DefaultTransferModes"/> there).
    /// </summary>
    [Fact]
    public void ExplicitFootAbsolute_OnToelessSource_OverridesVirtualFootFallback()
    {
        var skeleton = GeometricSolverTests.Zombie().Skeleton;
        var rig = GeometricSolverTests.SboxRig();
        var scene = GeometricSolverTests.SceneFromWorldFrame(
            skeleton, skeleton.RestWorld.ToArray(), "rest_frame");

        var toeless = GeometricSolverTests.DetectMap(GeometricSolverTests.Zombie(), "mixamo");
        toeless.RoleToBone.Remove(BoneRole.ToeL);
        toeless.RoleToBone.Remove(BoneRole.ToeR);

        // Fixture sanity: the fallback's trigger condition really holds — virtual source
        // foot primary (no mapped toe), real target anatomy (toes mapped on the rig).
        var (_, srcCanon, _) = GeometricSolverTests.NormCanon(skeleton, toeless);
        var (_, tgtCanon, _) = GeometricSolverTests.NormCanon(rig.Skeleton, rig.ToMappingResult());
        Assert.True(srcCanon.HasVirtualPrimary(BoneRole.FootL), "source foot primary should be virtual");
        Assert.False(tgtCanon.HasVirtualPrimary(BoneRole.FootL), "target foot primary should be real");

        // Explicit map = the defaults plus the feet pinned ABSOLUTE.
        var explicitModes = SolveOptions.DefaultTransferModes
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        explicitModes[BoneRole.FootL] = RoleTransferMode.AbsoluteDirection;
        explicitModes[BoneRole.FootR] = RoleTransferMode.AbsoluteDirection;

        var solver = new GeometricSolver();
        var fallbackRun = solver.Solve(scene, toeless, rig, new SolveOptions());
        var explicitRun = solver.Solve(scene, toeless, rig, new SolveOptions
        {
            TransferModes = explicitModes,
        });

        var (tgtNorm, _) = RestNormalizer.Normalize(rig.Skeleton, rig.ToMappingResult());
        var fallbackWorld = GeometricSolverTests.Fk(rig.Skeleton, fallbackRun.Frames[0]);
        var explicitWorld = GeometricSolverTests.Fk(rig.Skeleton, explicitRun.Frames[0]);

        var footBones = new HashSet<int>();
        foreach (var role in new[] { BoneRole.FootL, BoneRole.FootR })
        {
            var bone = rig.BoneForRole(role)!.Value;
            footBones.Add(bone);
            var restRot = tgtNorm.WorldRest[bone].Rot;

            // Null modes: the heuristic applies — at source rest the target foot keeps its
            // own rest carriage (delta transfer).
            var fallbackDeg = GeometricSolverTests.DegBetween(fallbackWorld[bone].Rot, restRot);
            Assert.True(fallbackDeg <= 0.5f,
                $"{role}: null TransferModes should fall back to delta (target rest carriage), "
                + $"off {fallbackDeg:F2} deg");

            // Explicit AbsoluteDirection: no fallback — the foot adopts the source's virtual
            // forward direction, away from the target's real rest pitch.
            var explicitDeg = GeometricSolverTests.DegBetween(explicitWorld[bone].Rot, restRot);
            Assert.True(explicitDeg > 1.5f,
                $"{role}: explicit AbsoluteDirection must solve absolute (fallback overrode the "
                + $"contract? only {explicitDeg:F2} deg from target rest)");
        }

        // The two runs differ ONLY on the foot bones — everywhere else the explicit map
        // reproduces the defaults exactly (toes are unmapped → rest locals in both).
        for (var i = 0; i < rig.Skeleton.Count; i++)
        {
            if (!footBones.Contains(i))
                Assert.Equal(fallbackRun.Frames[0][i], explicitRun.Frames[0][i]);
        }
    }

    /// <summary>
    /// A source whose REST head attitude is implausible as a neutral carriage (posed bind —
    /// here the zombie rest with its neck→head segment swung 30° further forward and 15°
    /// sideways, the Defenses.fbx fighting-guard class) makes the head's
    /// <see cref="RoleTransferMode.CharacterDeltaFromRest"/> default replay deltas from a
    /// posed reference: at source rest the output head would sit at the TARGET's neutral
    /// attitude while the source genuinely looks down — the reported "head looking up at an
    /// angle". Under the DEFAULT (null) modes the solver must fall back to ABSOLUTE gaze
    /// matching (output head adopts the source's posed direction); an explicit map
    /// reproducing the defaults is a contract and must NOT be overridden — the head replays
    /// the delta and keeps the target rest attitude.
    /// </summary>
    [Fact]
    public void PosedRestHeadSource_FallsBackToAbsoluteGaze_ExplicitMapWins()
    {
        var neutral = GeometricSolverTests.Zombie().Skeleton;
        var neutralMap = GeometricSolverTests.DetectMap(GeometricSolverTests.Zombie(), "mixamo");
        var rig = GeometricSolverTests.SboxRig();

        // Pose the rest head: swing the head joint about the neck so the neck→head segment
        // leans 30° further forward and 15° laterally (fwd ≈ 52°, lat ≈ 15° — both far
        // outside the measured neutral band of −3..27° fwd / ≤3° lat).
        var (_, neutralCanon, _) = GeometricSolverTests.NormCanon(neutral, neutralMap);
        var lat = Vector3.Normalize(Vector3.Cross(neutralCanon.CharacterUp, neutralCanon.CharacterForward));
        var swing = Quaternion.CreateFromAxisAngle(lat, 30f * MathF.PI / 180f) // up tips toward forward
                    * Quaternion.CreateFromAxisAngle(neutralCanon.CharacterForward, 15f * MathF.PI / 180f);
        var posed = PoseHead(neutral, neutralMap, swing);
        var posedMap = GeometricSolverTests.DetectMap(
            new SourceScene(posed, new[] { new Clip("none", 30f, false) }, 1f), "mixamo");
        var scene = GeometricSolverTests.SceneFromWorldFrame(
            posed, posed.RestWorld.ToArray(), "rest_frame");

        // Fixture sanity: both head primaries are real geometry (the fallback's precondition).
        var (_, srcCanon, _) = GeometricSolverTests.NormCanon(posed, posedMap);
        var (_, tgtCanon, _) = GeometricSolverTests.NormCanon(rig.Skeleton, rig.ToMappingResult());
        Assert.False(srcCanon.HasVirtualPrimary(BoneRole.Head), "source head primary should be real");
        Assert.False(tgtCanon.HasVirtualPrimary(BoneRole.Head), "target head primary should be real");

        var solver = new GeometricSolver();
        var fallbackRun = solver.Solve(scene, posedMap, rig, new SolveOptions());
        var explicitRun = solver.Solve(scene, posedMap, rig, new SolveOptions
        {
            TransferModes = SolveOptions.DefaultTransferModes.ToDictionary(kv => kv.Key, kv => kv.Value),
        });

        var (tgtNorm, _) = RestNormalizer.Normalize(rig.Skeleton, rig.ToMappingResult());
        var head = rig.BoneForRole(BoneRole.Head)!.Value;
        var restRot = tgtNorm.WorldRest[head].Rot;
        var fallbackWorld = GeometricSolverTests.Fk(rig.Skeleton, fallbackRun.Frames[0]);
        var explicitWorld = GeometricSolverTests.Fk(rig.Skeleton, explicitRun.Frames[0]);

        // Null modes: absolute gaze fallback — the output head adopts the source's posed
        // attitude, far from the target's neutral rest.
        var fallbackDeg = GeometricSolverTests.DegBetween(fallbackWorld[head].Rot, restRot);
        Assert.True(fallbackDeg > 10f,
            "null TransferModes on a posed-rest source should gaze-match absolutely "
            + $"(only {fallbackDeg:F2} deg from the target rest attitude)");

        // Explicit defaults map: the contract — CharacterDeltaFromRest replays the (identity)
        // delta, the head keeps the target's neutral rest attitude.
        var explicitDeg = GeometricSolverTests.DegBetween(explicitWorld[head].Rot, restRot);
        Assert.True(explicitDeg <= 0.5f,
            "explicit CharacterDeltaFromRest must not be overridden by the posed-rest fallback "
            + $"(off {explicitDeg:F2} deg from the target rest attitude)");

        // The NEUTRAL-rest zombie never triggers the fallback: defaults == explicit defaults.
        var neutralScene = GeometricSolverTests.SceneFromWorldFrame(
            neutral, neutral.RestWorld.ToArray(), "rest_frame");
        var neutralDefaults = solver.Solve(neutralScene, neutralMap, rig, new SolveOptions());
        var neutralHeadDeg = GeometricSolverTests.DegBetween(
            GeometricSolverTests.Fk(rig.Skeleton, neutralDefaults.Frames[0])[head].Rot, restRot);
        Assert.True(neutralHeadDeg <= 0.5f,
            $"neutral-rest source must keep the delta default (off {neutralHeadDeg:F2} deg)");
    }

    /// <summary>Rebuilds the skeleton with the head joint's rest swung about its parent
    /// (the neck) by <paramref name="swing"/> — descendant rest geometry rides along.</summary>
    private static Skel PoseHead(Skel skeleton, MappingResult map, Quaternion swing)
    {
        var headBone = map.RoleToBone[BoneRole.Head];
        var neckBone = skeleton[headBone].ParentIndex;
        Assert.True(neckBone >= 0, "fixture: head must have a parent");

        // World-space swing about the neck joint expressed on the head's rest LOCAL: rotate
        // the local offset/orientation through the parent's rest world rotation.
        var neckRot = skeleton.RestWorld[neckBone].Rot;
        var localSwing = MathQ.Normalize(
            Quaternion.Conjugate(neckRot) * swing * neckRot);

        var defs = new List<BoneDefinition>(skeleton.Count);
        foreach (var bone in skeleton.Bones)
        {
            var local = bone.RestLocal;
            if (bone.Index == headBone)
            {
                local = new XForm(
                    Vector3.Transform(local.Pos, localSwing),
                    MathQ.Normalize(localSwing * local.Rot));
            }
            defs.Add(new BoneDefinition(
                bone.Name, bone.ParentIndex < 0 ? null : skeleton[bone.ParentIndex].Name, local));
        }
        return Skel.Create(defs);
    }

    /// <summary>
    /// An EMPTY (non-null) map is the all-absolute legacy behavior (checkbox-off path): the
    /// solve runs, the delta-default roles (clavicles, neck) genuinely change versus the
    /// null-defaults run, and absolute roles stay bit-identical.
    /// </summary>
    [Fact]
    public void EmptyTransferModes_IsLegacyAllAbsolute_OnZombieClip()
    {
        var scene = GeometricSolverTests.Zombie();
        var srcMap = GeometricSolverTests.DetectMap(scene, "mixamo");
        var rig = GeometricSolverTests.SboxRig();

        var solver = new GeometricSolver();
        var defaults = solver.Solve(scene, srcMap, rig, new SolveOptions());
        var legacy = solver.Solve(scene, srcMap, rig, new SolveOptions
        {
            TransferModes = new Dictionary<BoneRole, RoleTransferMode>(), // empty = all absolute
        });

        Assert.True(legacy.FrameCount > 0);
        Assert.Equal(defaults.FrameCount, legacy.FrameCount);
        Assert.Equal(defaults.Fps, legacy.Fps);

        // The delta-default roles really switch to absolute: their output diverges from the
        // defaults run (each rig's clavicle line/neck base/ankle anatomy differs from the
        // source's, so absolute matching visibly drags them — the legacy "low shoulders" /
        // "bent feet" carriage).
        var sampleFrames = GeometricSolverTests.SampleFrames(defaults.FrameCount);
        foreach (var role in new[]
        {
            BoneRole.ClavicleL, BoneRole.ClavicleR, BoneRole.Neck, BoneRole.FootL, BoneRole.FootR,
        })
        {
            var bone = rig.BoneForRole(role)!.Value;
            var maxDeg = 0f;
            foreach (var f in sampleFrames)
            {
                maxDeg = MathF.Max(maxDeg, GeometricSolverTests.DegBetween(
                    defaults.Frames[f][bone].Rot, legacy.Frames[f][bone].Rot));
            }
            Assert.True(maxDeg > 2f,
                $"{role}: empty TransferModes should change the output vs the delta default "
                + $"(moved only {maxDeg:F2} deg — override not effective?)");
        }

        // Roles outside DefaultTransferModes were absolute already — their WORLD rotations
        // are unaffected by the map. (The head is mode-mapped since the head-carriage round:
        // its CharacterDeltaFromRest default vs legacy absolute differs only by the rigs'
        // small neutral head-anatomy divergence — too small for the >2° gate above, nonzero
        // for the identity gate here, so it belongs to neither list.)
        foreach (var f in sampleFrames)
        {
            var defaultsWorld = GeometricSolverTests.Fk(rig.Skeleton, defaults.Frames[f]);
            var legacyWorld = GeometricSolverTests.Fk(rig.Skeleton, legacy.Frames[f]);
            foreach (var role in new[]
            {
                BoneRole.LowerArmL, BoneRole.Spine0, BoneRole.UpperArmL, BoneRole.HandR, BoneRole.ToeL,
            })
            {
                var bone = rig.BoneForRole(role)!.Value;
                var deg = GeometricSolverTests.DegBetween(
                    defaultsWorld[bone].Rot, legacyWorld[bone].Rot);
                Assert.True(deg <= 1e-3f,
                    $"{role} @ frame {f}: absolute role should be identical across mode maps "
                    + $"(differs {deg:F4} deg)");
            }
        }
    }
}
