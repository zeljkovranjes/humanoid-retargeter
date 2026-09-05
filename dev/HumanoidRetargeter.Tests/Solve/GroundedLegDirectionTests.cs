using System.Numerics;
using System.Text;
using HumanoidRetargeter.Formats.Bvh;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Mapping;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using Xunit;
using Skel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Solve;

public class GroundedLegDirectionTests
{
    [Theory]
    [InlineData(20f)]
    [InlineData(-20f)]
    public void GroundingDoesNotTiltPlantedLegsWithTargetTorso(float lean)
    {
        var bytes = Encoding.UTF8.GetBytes(WalkFixture.SyntheticWalkBvh());
        var source = BvhImporter.Import(bytes);
        var definitions = source.Skeleton.Bones.Select(b =>
        {
            var rest = b.RestLocal;
            if (b.Name == "mixamorig:Spine")
                rest.Rot = Quaternion.CreateFromAxisAngle(Vector3.UnitX, lean * MathF.PI / 180f);
            return new BoneDefinition(b.Name, b.ParentIndex < 0 ? null : source.Skeleton[b.ParentIndex].Name, rest);
        });
        var skeleton = Skel.Create(definitions.ToArray());
        var (map, _) = Retargeter.ResolveMapping(skeleton);
        var target = new RetargetTargetSpec { Rig = TargetRig.FromSkeleton(skeleton, map), VmdlScale = RetargetTargetSpec.SboxSourceScale };

        float PlantedLegAngle(bool cleanup)
        {
            var result = Retargeter.Convert(new RetargetRequest
            {
                SourceData = bytes, SourceFileName = "walk.bvh", FootPlantCleanup = cleanup,
            }, target);
            Assert.True(result.Success);
            var world = new Pose(result.Clips[0].SolvedFrames![7]).ToWorld(skeleton);
            var direction = world[map.RoleToBone[BoneRole.FootR]].Pos
                - world[map.RoleToBone[BoneRole.LowerLegR]].Pos;
            return MathQ.AngleBetween(direction, -Vector3.UnitY) * 180f / MathF.PI;
        }

        Assert.True(PlantedLegAngle(false) > 10f); // Repro: torso lean contaminates the stance.
        Assert.True(PlantedLegAngle(true) < 1f);
    }
}
