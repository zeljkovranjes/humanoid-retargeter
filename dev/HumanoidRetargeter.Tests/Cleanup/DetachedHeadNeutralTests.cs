using System.Numerics;
using HumanoidRetargeter.Mapping;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Cleanup;

public class DetachedHeadNeutralTests
{
    [Fact]
    public void SmallDetachedHeadBindPitch_IsLeveledWithoutLosingAnimationDelta()
    {
        var headRest = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -5f * MathF.PI / 180f);
        var skeleton = SkeletonModel.Create(new[]
        {
            new BoneDefinition("scene", null, XForm.Identity),
            new BoneDefinition("hips", "scene", new XForm(new Vector3(0, 100, 0), Quaternion.Identity)),
            new BoneDefinition("spine", "hips", new XForm(new Vector3(0, 40, 0), Quaternion.Identity)),
            new BoneDefinition("neck", "spine", new XForm(new Vector3(0, 20, 0), Quaternion.Identity)),
            new BoneDefinition("leg_l", "hips", new XForm(new Vector3(10, -10, 0), Quaternion.Identity)),
            new BoneDefinition("leg_r", "hips", new XForm(new Vector3(-10, -10, 0), Quaternion.Identity)),
            new BoneDefinition("control_neck", "scene", XForm.Identity),
            new BoneDefinition("head", "control_neck", new XForm(new Vector3(0, 165, 0), headRest)),
        });
        var map = new MappingResult("test", MappingSource.Manual);
        map.RoleToBone[BoneRole.Hips] = 1;
        map.RoleToBone[BoneRole.Spine0] = 2;
        map.RoleToBone[BoneRole.Neck] = 3;
        map.RoleToBone[BoneRole.UpperLegL] = 4;
        map.RoleToBone[BoneRole.UpperLegR] = 5;
        map.RoleToBone[BoneRole.Head] = 7;
        var rig = TargetRig.FromSkeleton(skeleton, map);

        var animationDelta = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f);
        var frame = skeleton.Bones.Select(bone => bone.RestLocal).ToArray();
        frame[7] = new XForm(frame[7].Pos, MathQ.Normalize(animationDelta * headRest));

        HumanoidRetargeter.Retargeter.TestHook_FollowOrphans(
            rig, new List<XForm[]> { frame });

        var world = new Pose(frame).ToWorld(skeleton);
        var neutralRotation = MathQ.Normalize(Quaternion.Conjugate(animationDelta) * world[7].Rot);
        var gaze = Vector3.Transform(Vector3.UnitZ, neutralRotation);
        Assert.True(MathF.Abs(Vector3.Dot(gaze, Vector3.UnitY)) < 1e-4f);
        Assert.True(Vector3.Dot(gaze, Vector3.UnitZ) > 0.999f);
    }
}
