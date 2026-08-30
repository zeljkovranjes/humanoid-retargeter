using System.Collections.Generic;
using HumanoidRetargeter.Mapping;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Mapping;

public sealed class FightNightProfileTests
{
    [Fact]
    public void BoxerRigSelectsFightNightAndLeavesControlAndGloveShellBonesUnmapped()
    {
        Assert.Equal(33, ProfileLibrary.FightNight.Aliases.Count);

        var definitions = new List<BoneDefinition>();
        foreach (var aliases in ProfileLibrary.FightNight.Aliases.Values)
            definitions.Add(new BoneDefinition(aliases[0], null, XForm.Identity));

        foreach (var helper in new[]
        {
            "Reference", "AITrajectory", "Neck1", "LeftForeArmTwist",
            "LeftAnkleEffectorAux", "LeftHandThumbGlove", "LeftHandIndexGlove",
        })
        {
            definitions.Add(new BoneDefinition(helper, null, XForm.Identity));
        }

        var skeleton = SkeletonModel.Create(definitions);
        var detection = ProfileDetector.Detect(skeleton);

        Assert.NotNull(detection);
        Assert.Equal("fight_night", detection.Value.Profile.Name);
        Assert.Equal(1f, detection.Value.Result.Confidence);
        Assert.Equal(skeleton.IndexOf("LeftInHandMiddle"),
            detection.Value.Result.RoleToBone[BoneRole.MiddleMetaL]);
        Assert.DoesNotContain(skeleton.IndexOf("LeftHandThumbGlove"),
            detection.Value.Result.RoleToBone.Values);
        Assert.DoesNotContain(skeleton.IndexOf("LeftHandIndexGlove"),
            detection.Value.Result.RoleToBone.Values);
        Assert.DoesNotContain(skeleton.IndexOf("LeftAnkleEffectorAux"),
            detection.Value.Result.RoleToBone.Values);
    }
}
