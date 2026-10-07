using System.Numerics;
using HumanoidRetargeter.Core.Formats.Fbx;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Solve;
using HumanoidRetargeter.Core.Target;
using Xunit;
using Xunit.Abstractions;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Mapping;

/// <summary>
/// User repro (2026-07-04): the simpson rig (Auto-Rig Pro export) parks <c>head.x</c> at
/// chest height ~90cm BELOW <c>neck.x</c>, with the skin bind compensating — the mesh is
/// correct at rest, but every skull rotation replayed onto that bone sweeps the head
/// geometry on the 90cm lever arm ("the head/back is stretched and it stretched more when
/// I played the animation"). A head bone resting below the neck is not anatomy the solver
/// can drive; automatic mapping must refuse it so the neck (whose pivot IS at the skull
/// base on such rigs) carries head motion instead.
/// </summary>
public class ImpossibleHeadVetoTests
{
    private readonly ITestOutputHelper _out;
    public ImpossibleHeadVetoTests(ITestOutputHelper o) => _out = o;

    /// <summary>Mixamo-named minimal biped; <paramref name="headY"/> places the head joint.
    /// Uses profile-detected mapping, so the veto must apply to detected maps too.</summary>
    private static SkeletonModel BuildRig(float headY) => SkeletonModel.Create(new[]
    {
        // Y up, cm. Neck rests at world y=160; headY parks the head joint.
        new BoneDefinition("mixamorig:Hips", null, new XForm(new Vector3(0, 100, 0), Quaternion.Identity)),
        new BoneDefinition("mixamorig:Spine", "mixamorig:Hips", new XForm(new Vector3(0, 15, 0), Quaternion.Identity)),
        new BoneDefinition("mixamorig:Spine1", "mixamorig:Spine", new XForm(new Vector3(0, 15, 0), Quaternion.Identity)),
        new BoneDefinition("mixamorig:Spine2", "mixamorig:Spine1", new XForm(new Vector3(0, 15, 0), Quaternion.Identity)),
        new BoneDefinition("mixamorig:Neck", "mixamorig:Spine2", new XForm(new Vector3(0, 15, 0), Quaternion.Identity)),
        new BoneDefinition("mixamorig:Head", "mixamorig:Neck", new XForm(new Vector3(0, headY - 160f, 0), Quaternion.Identity)),
        new BoneDefinition("mixamorig:LeftShoulder", "mixamorig:Spine2", new XForm(new Vector3(5, 10, 0), Quaternion.Identity)),
        new BoneDefinition("mixamorig:LeftArm", "mixamorig:LeftShoulder", new XForm(new Vector3(10, 0, 0), Quaternion.Identity)),
        new BoneDefinition("mixamorig:LeftForeArm", "mixamorig:LeftArm", new XForm(new Vector3(25, 0, 0), Quaternion.Identity)),
        new BoneDefinition("mixamorig:LeftHand", "mixamorig:LeftForeArm", new XForm(new Vector3(25, 0, 0), Quaternion.Identity)),
        new BoneDefinition("mixamorig:RightShoulder", "mixamorig:Spine2", new XForm(new Vector3(-5, 10, 0), Quaternion.Identity)),
        new BoneDefinition("mixamorig:RightArm", "mixamorig:RightShoulder", new XForm(new Vector3(-10, 0, 0), Quaternion.Identity)),
        new BoneDefinition("mixamorig:RightForeArm", "mixamorig:RightArm", new XForm(new Vector3(-25, 0, 0), Quaternion.Identity)),
        new BoneDefinition("mixamorig:RightHand", "mixamorig:RightForeArm", new XForm(new Vector3(-25, 0, 0), Quaternion.Identity)),
        new BoneDefinition("mixamorig:LeftUpLeg", "mixamorig:Hips", new XForm(new Vector3(10, -5, 0), Quaternion.Identity)),
        new BoneDefinition("mixamorig:LeftLeg", "mixamorig:LeftUpLeg", new XForm(new Vector3(0, -45, 0), Quaternion.Identity)),
        new BoneDefinition("mixamorig:LeftFoot", "mixamorig:LeftLeg", new XForm(new Vector3(0, -45, 0), Quaternion.Identity)),
        new BoneDefinition("mixamorig:RightUpLeg", "mixamorig:Hips", new XForm(new Vector3(-10, -5, 0), Quaternion.Identity)),
        new BoneDefinition("mixamorig:RightLeg", "mixamorig:RightUpLeg", new XForm(new Vector3(0, -45, 0), Quaternion.Identity)),
        new BoneDefinition("mixamorig:RightFoot", "mixamorig:RightLeg", new XForm(new Vector3(0, -45, 0), Quaternion.Identity)),
    });

    [Fact]
    public void HeadParkedBelowNeck_IsVetoed()
    {
        // Neck rests at world y=160; the head joint parked at chest height (y=110).
        var skeleton = BuildRig(headY: 110f);
        var (map, _) = HumanoidRetargeter.Core.Retargeter.ResolveMapping(skeleton);
        _out.WriteLine($"profile={map.ProfileName} notes=[{string.Join(" | ", map.Notes)}]");
        Assert.False(map.RoleToBone.ContainsKey(BoneRole.Head),
            "a head bone resting below the neck must not be mapped");
        Assert.Contains(map.Notes, n => n.Contains("Head", StringComparison.OrdinalIgnoreCase));
        // The neck of a broken skull region goes with it: on the real rig it skins the
        // skull AND a torso column, and driving it kinked the whole back (see
        // Simpson_SolvedNeckBone_StaysAtRest). The rest of the body chain is untouched.
        Assert.False(map.RoleToBone.ContainsKey(BoneRole.Neck),
            "the neck of a vetoed skull region must not be driven either");
        Assert.True(map.RoleToBone.ContainsKey(BoneRole.Hips));
        Assert.True(map.RoleToBone.ContainsKey(BoneRole.Spine2));
    }

    [Fact]
    public void HeadAboveNeck_IsKept()
    {
        var skeleton = BuildRig(headY: 175f);
        var (map, _) = HumanoidRetargeter.Core.Retargeter.ResolveMapping(skeleton);
        Assert.True(map.RoleToBone.ContainsKey(BoneRole.Head), "a normal head must stay mapped");
    }

    [Fact]
    public void ExplicitOverride_IsNeverVetoed()
    {
        var skeleton = BuildRig(headY: 110f);
        var (detected, _) = HumanoidRetargeter.Core.Retargeter.ResolveMapping(BuildRig(headY: 175f));
        // Re-point the override at the parked-head skeleton: same indices, explicit source.
        var explicitMap = new MappingResult(detected.ProfileName, MappingSource.Manual);
        foreach (var (role, index) in detected.RoleToBone)
            explicitMap.RoleToBone[role] = index;
        var (map, _) = HumanoidRetargeter.Core.Retargeter.ResolveMapping(skeleton, explicitMap);
        Assert.True(map.RoleToBone.ContainsKey(BoneRole.Head),
            "explicit mappings are authoritative — no geometric second-guessing");
    }

    /// <summary>The user-visible contract on the real asset (local-only): the parked head
    /// bone must stay at its rest local through an entire solved clip, so the skull rides
    /// the neck instead of sweeping on the 90cm lever arm.</summary>
    [Fact]
    public void Simpson_SolvedHeadBone_StaysAtRest()
    {
        var fbx = TestUtil.RepoFile("dev", "corpus", "user_rigs", "simpson", "source", "C8V7NUC53TF8TTF3BXDMVGQIQ.fbx");
        var clipPath = TestUtil.RepoFile("dev", "corpus", "mixamo", "Surprised.fbx");
        if (!File.Exists(fbx) || !File.Exists(clipPath)) return; // local-only

        var scene = FbxImporter.Import(File.ReadAllBytes(fbx));
        var (map, _) = HumanoidRetargeter.Core.Retargeter.ResolveMapping(scene.Skeleton);
        Assert.False(map.RoleToBone.ContainsKey(BoneRole.Head),
            "simpson's head.x rests 90cm below neck.x - it must be vetoed");

        var headIndex = Enumerable.Range(0, scene.Skeleton.Count)
            .Single(i => scene.Skeleton[i].Name == "head.x");
        var rig = TargetRig.FromSkeleton(scene.Skeleton, map);
        var target = new HumanoidRetargeter.Core.RetargetTargetSpec
        { Rig = rig, VmdlScale = 0.3937f, DefaultRootBone = scene.Skeleton[0].Name };
        var batch = HumanoidRetargeter.Core.Retargeter.ConvertBatch(
            new[] { new HumanoidRetargeter.Core.RetargetRequest {
                SourceData = File.ReadAllBytes(clipPath), SourceFileName = "Surprised.fbx" } },
            target, new HumanoidRetargeter.Core.BatchOptions { DmxFolderRelative = "animations" });
        var clip = batch.Clips.Single();
        Assert.True(clip.Success, clip.Error);

        var rest = rig.Skeleton[headIndex].RestLocal.Rot;
        foreach (var frame in clip.SolvedFrames!)
        {
            var swing = 2f * MathF.Acos(MathF.Min(1f, MathF.Abs(Quaternion.Dot(
                Quaternion.Normalize(frame[headIndex].Rot), Quaternion.Normalize(rest))))) * 180f / MathF.PI;
            Assert.True(swing < 0.01f, $"head.x moved {swing:0.##} deg off rest");
        }
    }

    /// <summary>The neck of a vetoed skull region must rest with the head. On this rig
    /// neck.x skins the skull AND a whole torso column (690 verts, hips to crown), so any
    /// solved neck bend shears half the torso — measured: the source distributes its ~20°
    /// idle hunch through its spine while the target spine stays near upright, leaving a
    /// ~19° LOCAL neck kink that rendered as a huge hump grafted onto the back (the
    /// user's "back of homer looks weird"). Skull attitude rides the spine instead.</summary>
    [Fact]
    public void Simpson_SolvedNeckBone_StaysAtRest()
    {
        var fbx = TestUtil.RepoFile("dev", "corpus", "user_rigs", "simpson", "source", "C8V7NUC53TF8TTF3BXDMVGQIQ.fbx");
        var clipPath = TestUtil.RepoFile("dev", "corpus", "mixamo", "Surprised.fbx");
        if (!File.Exists(fbx) || !File.Exists(clipPath)) return; // local-only

        var scene = FbxImporter.Import(File.ReadAllBytes(fbx));
        var (map, _) = HumanoidRetargeter.Core.Retargeter.ResolveMapping(scene.Skeleton);
        var rig = TargetRig.FromSkeleton(scene.Skeleton, map);
        var target = new HumanoidRetargeter.Core.RetargetTargetSpec
        { Rig = rig, VmdlScale = 0.3937f, DefaultRootBone = scene.Skeleton[0].Name };
        var batch = HumanoidRetargeter.Core.Retargeter.ConvertBatch(
            new[] { new HumanoidRetargeter.Core.RetargetRequest {
                SourceData = File.ReadAllBytes(clipPath), SourceFileName = "Surprised.fbx" } },
            target, new HumanoidRetargeter.Core.BatchOptions { DmxFolderRelative = "animations" });
        var clip = batch.Clips.Single();
        Assert.True(clip.Success, clip.Error);

        Assert.False(map.RoleToBone.ContainsKey(BoneRole.Neck), "neck must be vetoed with the head");
        var neck = Enumerable.Range(0, scene.Skeleton.Count)
            .Single(i => scene.Skeleton[i].Name == "neck.x");
        var rest = rig.Skeleton[neck].RestLocal.Rot;
        foreach (var frame in clip.SolvedFrames!)
        {
            var swing = 2f * MathF.Acos(MathF.Min(1f, MathF.Abs(Quaternion.Dot(
                Quaternion.Normalize(frame[neck].Rot), Quaternion.Normalize(rest))))) * 180f / MathF.PI;
            Assert.True(swing < 0.01f, $"neck.x moved {swing:0.##} deg off rest");
        }
    }
}
