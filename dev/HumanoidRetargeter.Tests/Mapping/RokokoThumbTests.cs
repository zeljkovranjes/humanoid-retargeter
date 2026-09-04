using System.Numerics;
using HumanoidRetargeter.Mapping;
using HumanoidRetargeter.Solve;
using Xunit;

namespace HumanoidRetargeter.Tests.Mapping;

public class RokokoThumbTests
{
    [Fact]
    public void CoincidentSingleJointThumb_UsesItsEndSiteDirection()
    {
        var skeleton = MappingFixtures.FromWorldPositions(new[]
        {
            ("Hips", (string?)null, new Vector3(0, 100, 0)),
            ("Spine", "Hips", new Vector3(0, 125, 0)),
            ("Neck", "Spine", new Vector3(0, 150, 0)),
            ("Head", "Neck", new Vector3(0, 165, 0)),
            ("LeftUpLeg", "Hips", new Vector3(10, 90, 0)),
            ("LeftLeg", "LeftUpLeg", new Vector3(10, 50, 0)),
            ("LeftFoot", "LeftLeg", new Vector3(10, 5, 5)),
            ("RightUpLeg", "Hips", new Vector3(-10, 90, 0)),
            ("RightLeg", "RightUpLeg", new Vector3(-10, 50, 0)),
            ("RightFoot", "RightLeg", new Vector3(-10, 5, 5)),
            ("LeftShoulder", "Spine", new Vector3(5, 140, 0)),
            ("LeftArm", "LeftShoulder", new Vector3(20, 140, 0)),
            ("LeftForeArm", "LeftArm", new Vector3(40, 140, 0)),
            ("LeftHand", "LeftForeArm", new Vector3(60, 140, 0)),
            ("LThumb", "LeftHand", new Vector3(60, 140, 0)),
            ("LThumb_end", "LThumb", new Vector3(64, 140, 4)),
            ("RightShoulder", "Spine", new Vector3(-5, 140, 0)),
            ("RightArm", "RightShoulder", new Vector3(-20, 140, 0)),
            ("RightForeArm", "RightArm", new Vector3(-40, 140, 0)),
            ("RightHand", "RightForeArm", new Vector3(-60, 140, 0)),
            ("RThumb", "RightHand", new Vector3(-60, 140, 0)),
            ("RThumb_end", "RThumb", new Vector3(-64, 140, 4)),
        });

        var (map, _) = HumanoidRetargeter.Retargeter.ResolveMapping(skeleton);
        Assert.Equal("rokoko_bvh", map.ProfileName);
        Assert.Equal(skeleton.IndexOf("LThumb"), map.RoleToBone[BoneRole.ThumbProxL]);
        Assert.Equal(skeleton.IndexOf("RThumb"), map.RoleToBone[BoneRole.ThumbProxR]);

        var frames = CanonicalFrames.Build(skeleton, map);
        Assert.True(frames.Has(BoneRole.HandL));
        Assert.True(frames.Has(BoneRole.HandR));
        Assert.True(frames.Has(BoneRole.ThumbProxL));
        Assert.True(frames.Has(BoneRole.ThumbProxR));
    }
}
