using System.Numerics;
using HumanoidRetargeter.Cleanup;
using HumanoidRetargeter.Mapping;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Cleanup;

public class CoincidentDeformFollowerTests
{
    [Fact]
    public void DeformSiblingAtMappedMechanismPivot_FollowsFullBend()
    {
        var skeleton = SkeletonModel.Create(new[]
        {
            new BoneDefinition("hips", null, new XForm(Vector3.Zero, Quaternion.Identity)),
            new BoneDefinition("upper_arm", "hips", new XForm(new Vector3(10, 0, 0), Quaternion.Identity)),
            new BoneDefinition("MCH_forearm", "upper_arm", new XForm(new Vector3(30, 0, 0), Quaternion.Identity)),
            new BoneDefinition("radius", "upper_arm", new XForm(new Vector3(30.1f, 0, 0), Quaternion.Identity)),
            new BoneDefinition("hand", "MCH_forearm", new XForm(new Vector3(25, 0, 0), Quaternion.Identity)),
        });
        var map = new MappingResult("test", MappingSource.Manual);
        map.RoleToBone[BoneRole.Hips] = 0;
        map.RoleToBone[BoneRole.UpperArmL] = 1;
        map.RoleToBone[BoneRole.LowerArmL] = 2;
        map.RoleToBone[BoneRole.HandL] = 4;
        var rig = TargetRig.FromSkeleton(skeleton, map);

        var frame = skeleton.Bones.Select(bone => bone.RestLocal).ToArray();
        var bend = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 3f);
        frame[2] = new XForm(frame[2].Pos, bend);

        var driven = TwistBoneFollow.Apply(new List<XForm[]> { frame }, rig, excluded: null);

        Assert.Equal(1, driven);
        Assert.True(MathF.Abs(Quaternion.Dot(
            Quaternion.Normalize(frame[3].Rot), Quaternion.Normalize(bend))) > 0.999f);
        Assert.Equal(skeleton[3].RestLocal.Pos, frame[3].Pos);
    }
}
