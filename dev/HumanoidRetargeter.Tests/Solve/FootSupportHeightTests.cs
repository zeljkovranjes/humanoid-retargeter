using System.Numerics;
using System.Text;
using HumanoidRetargeter.Formats.Bvh;
using HumanoidRetargeter.Mapping;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using Xunit;
using Skel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Solve;

public class FootSupportHeightTests
{
    [Fact]
    public void DifferentLegProportionsPreserveEachFootHeightIncludingSwing()
    {
        var bytes = Encoding.UTF8.GetBytes(WalkFixture.SyntheticWalkBvh());
        var source = BvhImporter.Import(bytes);
        var definitions = source.Skeleton.Bones.Select(b =>
        {
            var rest = b.RestLocal;
            if (b.Name.EndsWith("LeftLeg") || b.Name.EndsWith("RightLeg")) rest.Pos *= .85f;
            if (b.Name.EndsWith("LeftFoot") || b.Name.EndsWith("RightFoot")) rest.Pos *= 1.15f;
            return new BoneDefinition(b.Name, b.ParentIndex < 0 ? null : source.Skeleton[b.ParentIndex].Name, rest);
        }).ToArray();
        var target = Skel.Create(definitions);
        var (map, _) = Retargeter.ResolveMapping(target);
        var result = Retargeter.Convert(new RetargetRequest
        {
            SourceData = bytes, SourceFileName = "walk.bvh", FootPlantCleanup = true,
        }, new RetargetTargetSpec { Rig = TargetRig.FromSkeleton(target, map), VmdlScale = 1f });
        Assert.True(result.Success);
        var frames = result.Clips[0].SolvedFrames!;
        var placement = source.Clips[0].Frames.SelectMany(p => new Pose(p).ToWorld(source.Skeleton)).Min(p => p.Pos.Y)
            - source.Skeleton.RestWorld.Min(p => p.Pos.Y);
        foreach (var role in new[] { BoneRole.ToeL, BoneRole.ToeR })
        {
            var bone = map.RoleToBone[role];
            for (var f = 0; f < frames.Count; f++)
            {
                var actual = new Pose(frames[f]).ToWorld(target)[bone].Pos.Y;
                var authored = new Pose(source.Clips[0].Frames[f]).ToWorld(source.Skeleton)[bone].Pos.Y;
                // BVH placement is measured against its motion floor. Both rigs have
                // equal total leg length but different thigh/calf proportions.
                Assert.True(MathF.Abs(actual - (authored - placement)) < .1f,
                    $"{role} frame {f}: actual {actual}, expected {authored - placement}");
            }
        }
    }
}
