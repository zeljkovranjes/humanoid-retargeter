using System.Numerics;
using HumanoidRetargeter.Mapping;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Cleanup;

public class DisconnectedMappedBranchTests
{
    [Fact]
    public void MappedHeadOnControlTree_FollowsConnectedNeckWithoutLosingRotation()
    {
        var skeleton = SkeletonModel.Create(new[]
        {
            new BoneDefinition("scene", null, new XForm(Vector3.Zero, Quaternion.Identity)),
            new BoneDefinition("hips", "scene", new XForm(Vector3.Zero, Quaternion.Identity)),
            new BoneDefinition("spine", "hips", new XForm(new Vector3(0, 50, 0), Quaternion.Identity)),
            new BoneDefinition("neck", "spine", new XForm(new Vector3(0, 20, 0), Quaternion.Identity)),
            new BoneDefinition("control_neck", "scene", new XForm(Vector3.Zero, Quaternion.Identity)),
            new BoneDefinition("head", "control_neck", new XForm(new Vector3(0, 75, 0), Quaternion.Identity)),
        });
        var map = new MappingResult("test", MappingSource.Manual);
        map.RoleToBone[BoneRole.Hips] = 1;
        map.RoleToBone[BoneRole.Spine0] = 2;
        map.RoleToBone[BoneRole.Neck] = 3;
        map.RoleToBone[BoneRole.Head] = 5;
        var rig = TargetRig.FromSkeleton(skeleton, map);

        var frame = skeleton.Bones.Select(bone => bone.RestLocal).ToArray();
        frame[1] = new XForm(new Vector3(10, 0, 0), frame[1].Rot);
        frame[3] = new XForm(frame[3].Pos,
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f));
        var solvedHeadRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f);
        frame[5] = new XForm(frame[5].Pos, solvedHeadRotation);

        HumanoidRetargeter.Retargeter.TestHook_FollowOrphans(
            rig, new List<XForm[]> { frame });

        var world = new HumanoidRetargeter.Skeleton.Pose(frame).ToWorld(skeleton);
        var headOffset = world[5].Pos - world[3].Pos;
        Assert.True((headOffset - new Vector3(-5, 0, 0)).Length() < 0.001f);
        Assert.True(MathF.Abs(Quaternion.Dot(
            Quaternion.Normalize(world[5].Rot), solvedHeadRotation)) > 0.999f);
    }
}
