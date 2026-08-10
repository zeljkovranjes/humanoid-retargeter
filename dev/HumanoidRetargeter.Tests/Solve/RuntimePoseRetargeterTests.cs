using System.Numerics;
using HumanoidRetargeter.Mapping;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Solve;
using HumanoidRetargeter.Target;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Solve;

public sealed class RuntimePoseRetargeterTests
{
    [Fact]
    public void RuntimeFrame_MatchesExistingClipSolverIncludingRootMotion()
    {
        var (source, sourceMap, target) = Fixture();
        var moving = RestLocals(source);
        moving[sourceMap.RoleToBone[BoneRole.Hips]] = new XForm(new Vector3(25f, 100f, 8f), Quaternion.Identity);
        moving[sourceMap.RoleToBone[BoneRole.UpperArmL]] = new XForm(
            moving[sourceMap.RoleToBone[BoneRole.UpperArmL]].Pos,
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.4f));

        var clip = new Clip("runtime", 30f, false, new List<XForm[]> { RestLocals(source), moving });
        var scene = new SourceScene(source, new[] { clip }, 1f);
        var expected = new GeometricSolver().Solve(scene, sourceMap, target, new SolveOptions()).Frames[1];

        var runtime = new RuntimePoseRetargeter(source, sourceMap, target, RestLocals(source));
        var actual = new XForm[target.Skeleton.Count];
        runtime.Retarget(moving, actual);

        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < actual.Length; i++)
        {
            Assert.True(Vector3.Distance(expected[i].Pos, actual[i].Pos) < 1e-4f, $"bone {i} position");
            Assert.True(MathQ.AngleBetween(expected[i].Rot, actual[i].Rot) < 1e-4f, $"bone {i} rotation");
        }
    }

    [Fact]
    public void MissingOptionalBones_RetargetsRemainingHumanoid()
    {
        var (source, sourceMap, target) = Fixture(includeToes: false);
        var runtime = new RuntimePoseRetargeter(source, sourceMap, target);
        var destination = new XForm[target.Skeleton.Count];

        Assert.True(runtime.TryRetarget(RestLocals(source), destination, out var error), error);
        Assert.All(destination, x => Assert.True(float.IsFinite(x.Pos.X + x.Rot.W)));
    }

    [Fact]
    public void InvalidSourceSize_FailsWithoutMutatingDestination()
    {
        var (source, sourceMap, target) = Fixture();
        var runtime = new RuntimePoseRetargeter(source, sourceMap, target);
        var sentinel = new XForm(new Vector3(123f), Quaternion.Identity);
        var destination = Enumerable.Repeat(sentinel, target.Skeleton.Count).ToArray();

        Assert.False(runtime.TryRetarget(RestLocals(source).AsSpan(1), destination, out var error));
        Assert.Contains("expected", error, StringComparison.OrdinalIgnoreCase);
        Assert.All(destination, x => Assert.Equal(sentinel, x));
    }

    [Fact]
    public void InvalidMapping_FailsAtPlanBoundary()
    {
        var (source, _, target) = Fixture();
        var invalid = new MappingResult("invalid", MappingSource.Manual);
        invalid.RoleToBone[BoneRole.Hips] = source.Count + 10;

        var ex = Assert.Throws<ArgumentException>(() => new RuntimePoseRetargeter(source, invalid, target));
        Assert.Contains("outside", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static (SkeletonModel Source, MappingResult SourceMap, TargetRig Target) Fixture(bool includeToes = true)
    {
        var definitions = new List<BoneDefinition>
        {
            new("hips", null, new XForm(new Vector3(0, 100, 0), Quaternion.Identity)),
            new("spine", "hips", new XForm(new Vector3(0, 20, 0), Quaternion.Identity)),
            new("head", "spine", new XForm(new Vector3(0, 35, 0), Quaternion.Identity)),
            new("upperarm_l", "spine", new XForm(new Vector3(-15, 15, 0), Quaternion.Identity)),
            new("lowerarm_l", "upperarm_l", new XForm(new Vector3(-25, 0, 0), Quaternion.Identity)),
            new("hand_l", "lowerarm_l", new XForm(new Vector3(-20, 0, 0), Quaternion.Identity)),
            new("upperarm_r", "spine", new XForm(new Vector3(15, 15, 0), Quaternion.Identity)),
            new("lowerarm_r", "upperarm_r", new XForm(new Vector3(25, 0, 0), Quaternion.Identity)),
            new("hand_r", "lowerarm_r", new XForm(new Vector3(20, 0, 0), Quaternion.Identity)),
            new("thigh_l", "hips", new XForm(new Vector3(-10, -10, 0), Quaternion.Identity)),
            new("calf_l", "thigh_l", new XForm(new Vector3(0, -40, 0), Quaternion.Identity)),
            new("foot_l", "calf_l", new XForm(new Vector3(0, -40, 0), Quaternion.Identity)),
            new("thigh_r", "hips", new XForm(new Vector3(10, -10, 0), Quaternion.Identity)),
            new("calf_r", "thigh_r", new XForm(new Vector3(0, -40, 0), Quaternion.Identity)),
            new("foot_r", "calf_r", new XForm(new Vector3(0, -40, 0), Quaternion.Identity)),
        };
        if (includeToes)
        {
            definitions.Add(new BoneDefinition("toe_l", "foot_l", new XForm(new Vector3(0, 0, 15), Quaternion.Identity)));
            definitions.Add(new BoneDefinition("toe_r", "foot_r", new XForm(new Vector3(0, 0, 15), Quaternion.Identity)));
        }

        var source = SkeletonModel.Create(definitions);
        var map = AutoMapper.Map(source);

        var targetDefs = definitions.Select(d => new BoneDefinition(
            "target_" + d.Name,
            d.ParentName is null ? null : "target_" + d.ParentName,
            new XForm(d.RestLocal.Pos * 1.35f, d.RestLocal.Rot))).ToArray();
        var targetSkeleton = SkeletonModel.Create(targetDefs);
        var targetMap = AutoMapper.Map(targetSkeleton);
        var target = TargetRig.FromSkeleton(targetSkeleton, targetMap);
        return (source, map, target);
    }

    private static XForm[] RestLocals(SkeletonModel skeleton)
    {
        var pose = new XForm[skeleton.Count];
        for (var i = 0; i < pose.Length; i++)
            pose[i] = skeleton[i].RestLocal;
        return pose;
    }
}
