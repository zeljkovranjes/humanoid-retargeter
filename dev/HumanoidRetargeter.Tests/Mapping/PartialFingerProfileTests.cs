using System.Numerics;
using HumanoidRetargeter.Mapping;
using Xunit;

namespace HumanoidRetargeter.Tests.Mapping;

public class PartialFingerProfileTests
{
    [Fact]
    public void MissingChainsAreCompletedWithoutReplacingExistingDigits()
    {
        var bones = new List<(string, string?, Vector3)> { ("hand", null, Vector3.Zero) };
        foreach (var (name, z) in new[] { ("thumb", 3f), ("rayA", 1f), ("rayB", -2f) })
            for (var i = 1; i <= 4; i++)
                bones.Add(($"{name}{i}", i == 1 ? "hand" : $"{name}{i - 1}", new(i + 1, 0, z)));
        var rig = MappingFixtures.FromWorldPositions(bones);
        var map = new MappingResult("partial", MappingSource.Preset);
        map.RoleToBone.Add(BoneRole.HandL, rig.IndexOf("hand"));
        map.RoleToBone.Add(BoneRole.ThumbProxL, rig.IndexOf("thumb1"));
        Complete(rig, map);
        Assert.Equal(rig.IndexOf("thumb1"), map.RoleToBone[BoneRole.ThumbProxL]);
        Assert.False(map.RoleToBone.ContainsKey(BoneRole.ThumbMidL));
        Assert.Equal(rig.IndexOf("rayA1"), map.RoleToBone[BoneRole.IndexProxL]);
        Assert.Equal(rig.IndexOf("rayB3"), map.RoleToBone[BoneRole.MiddleDistL]);
        Assert.DoesNotContain(rig.IndexOf("rayA4"), map.RoleToBone.Values);
        Assert.Equal(map.RoleToBone.Count, map.RoleToBone.Values.Distinct().Count());
    }

    [Fact]
    public void ExistingRoleOnInferredChainPreventsDoubleAssignment()
    {
        var bones = new List<(string, string?, Vector3)> { ("hand", null, Vector3.Zero) };
        foreach (var (name, z) in new[] { ("thumb", 3f), ("ray", -2f) })
            for (var i = 1; i <= 3; i++)
                bones.Add(($"{name}{i}", i == 1 ? "hand" : $"{name}{i - 1}", new(i, 0, z)));
        var rig = MappingFixtures.FromWorldPositions(bones);
        var map = new MappingResult("partial", MappingSource.Preset);
        map.RoleToBone.Add(BoneRole.HandL, rig.IndexOf("hand"));
        map.RoleToBone.Add(BoneRole.MiddleProxL, rig.IndexOf("ray1"));
        Complete(rig, map);
        Assert.False(map.RoleToBone.ContainsKey(BoneRole.IndexProxL));
        Assert.Equal(rig.IndexOf("ray1"), map.RoleToBone[BoneRole.MiddleProxL]);
    }
    static void Complete(HumanoidRetargeter.Skeleton.Skeleton rig, MappingResult map)
        => typeof(AutoMapper).GetMethod("CompleteFingersByTopology",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(null, new object[] { rig, map });
}
