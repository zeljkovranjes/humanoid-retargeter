using System.Numerics;
using HumanoidRetargeter.Mapping;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Target;

public class StockBindPoseTests
{
    private const string RigJson = """
    { "name": "stock", "bones": [
        { "name": "pelvis", "parent": null, "class": "Animated", "role": "Hips", "local_pos": [0, 100, 0], "local_rot_xyzw": [0, 0, 0, 1], "tail_world": [0, 110, 0] },
        { "name": "helper", "parent": "pelvis", "class": "ConstraintDriven", "local_pos": [1, 0, 0], "local_rot_xyzw": [0, 0, 0, 1] },
        { "name": "root_IK", "parent": null, "class": "IkBaked", "local_pos": [0, 0, 0], "local_rot_xyzw": [0, 0, 0, 1] }
    ] }
    """;

    [Fact]
    public void RebindingPreservesCuratedHelpersAndAdoptsCompiledRest()
    {
        var stock = TargetRig.Load(RigJson);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
        var definitions = stock.Skeleton.Bones.Select(b => new BoneDefinition(b.Name,
            b.ParentIndex < 0 ? null : stock.Skeleton[b.ParentIndex].Name,
            b.ParentIndex < 0 ? new XForm(Vector3.Transform(b.RestLocal.Pos, rotation), rotation) : b.RestLocal)).ToList();
        definitions.Add(new BoneDefinition("custom_extra", "pelvis", XForm.Identity));
        var bind = SkeletonModel.Create(definitions);
        var rig = stock.WithBindPose(bind);
        Assert.Same(bind, rig.Skeleton);
        Assert.True(rig.HelpersAreConstraintDriven);
        Assert.Equal(BoneClass.ConstraintDriven, rig.ClassOf(bind.IndexOf("helper")));
        Assert.Equal(BoneClass.IkBaked, rig.ClassOf(bind.IndexOf("root_IK")));
        Assert.Equal(BoneClass.Animated, rig.ClassOf(bind.IndexOf("custom_extra")));
        Assert.Equal(bind.IndexOf("pelvis"), rig.BoneForRole(BoneRole.Hips));
        Assert.True(Vector3.Distance(new Vector3(-110, 0, 0), rig.TailWorldOf(bind.IndexOf("pelvis"))!.Value) < .001f);
        Assert.Equal(new Vector3(0, 100, 0), stock.Skeleton.RestWorld[stock.Skeleton.IndexOf("pelvis")].Pos);
    }

    [Fact]
    public void MissingMappedBonesFailRatherThanDroppingMotion()
        => Assert.Throws<ArgumentException>(() => TargetRig.Load(RigJson).WithBindPose(
            SkeletonModel.Create(new[] { new BoneDefinition("unrelated", null, XForm.Identity) })));

    [Fact]
    public void PrefabMeshUsesTheSameDmxRootCorrectionAsAnExplicitMesh()
    {
        var rig = TargetRig.Load(RigJson);
        var frames = new[] { rig.Skeleton.Bones.Select(b => b.RestLocal).ToArray() };
        var prefab = new RetargetTargetSpec { Rig = rig, VmdlScale = .3937f, CompensateDmxRootYaw = true };
        var explicitMesh = new RetargetTargetSpec { Rig = rig, VmdlScale = .3937f, MeshFilePath = "body.fbx" };
        var expected = Retargeter.TestHook_CompensateEmbeddedMeshRootYaw(frames, explicitMesh);
        var actual = Retargeter.TestHook_CompensateEmbeddedMeshRootYaw(frames, prefab);
        Assert.Equal(expected[0], actual[0]);
        Assert.NotEqual(frames[0][0].Rot, actual[0][0].Rot);
        Assert.Equal(rig.Skeleton[0].RestLocal, frames[0][0]);
    }
}
