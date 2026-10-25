using System.Numerics;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Solve;
using HumanoidRetargeter.Core.Target;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

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

    [Fact]
    public void AlignFacingToSource_TurnsATargetBoundFacingAnotherWay()
    {
        var (source, sourceMap, _) = Fixture();
        var turned = TurnedTarget(source, MathF.PI / 2f);

        var plain = new RuntimePoseRetargeter(source, sourceMap, turned);
        var aligned = new RuntimePoseRetargeter(source, sourceMap, turned, alignFacingToSource: true);

        var plainPose = new XForm[turned.Skeleton.Count];
        var alignedPose = new XForm[turned.Skeleton.Count];
        plain.Retarget(RestLocals(source), plainPose);
        aligned.Retarget(RestLocals(source), alignedPose);

        // The hands' left-to-right line shows which way a body faces
        var sourceSide = Side(source, source.RestWorld.ToArray(), "hand_l", "hand_r");
        var plainSide = Side(turned.Skeleton, World(turned.Skeleton, plainPose), "target_hand_l", "target_hand_r");
        var alignedSide = Side(turned.Skeleton, World(turned.Skeleton, alignedPose), "target_hand_l", "target_hand_r");

        Assert.True(plain.FacingCorrection.IsIdentity);
        Assert.True(MathQ.AngleBetween(sourceSide, plainSide) > 1.4f, "without alignment the target keeps its own facing");
        Assert.True(MathQ.AngleBetween(sourceSide, alignedSide) < 1e-3f, "aligned, the target faces like the source");
    }

    [Fact]
    public void AlignFacingToSource_LeavesATargetFacingTheSameWayUnchanged()
    {
        var (source, sourceMap, target) = Fixture();
        var plain = new RuntimePoseRetargeter(source, sourceMap, target);
        var aligned = new RuntimePoseRetargeter(source, sourceMap, target, alignFacingToSource: true);

        var plainPose = new XForm[target.Skeleton.Count];
        var alignedPose = new XForm[target.Skeleton.Count];
        plain.Retarget(RestLocals(source), plainPose);
        aligned.Retarget(RestLocals(source), alignedPose);

        Assert.True(aligned.FacingCorrection.IsIdentity);
        for (var i = 0; i < plainPose.Length; i++)
        {
            Assert.True(Vector3.Distance(plainPose[i].Pos, alignedPose[i].Pos) < 1e-4f, $"bone {i} position");
            Assert.True(MathQ.AngleBetween(plainPose[i].Rot, alignedPose[i].Rot) < 1e-4f, $"bone {i} rotation");
        }
    }

    // The fixture's target, its whole bind turned about the up axis (the fixture is Y-up)
    private static TargetRig TurnedTarget(SkeletonModel source, float radians)
    {
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, radians);
        var definitions = source.Bones.Select(b => new BoneDefinition(
            "target_" + b.Name,
            b.ParentIndex < 0 ? null : "target_" + source[b.ParentIndex].Name,
            b.ParentIndex < 0
                ? new XForm(Vector3.Transform(b.RestLocal.Pos, turn), turn * b.RestLocal.Rot)
                : b.RestLocal)).ToArray();

        var skeleton = SkeletonModel.Create(definitions);
        return TargetRig.FromSkeleton(skeleton, AutoMapper.Map(skeleton));
    }

    private static XForm[] World(SkeletonModel skeleton, XForm[] locals)
    {
        var world = new XForm[locals.Length];
        for (var i = 0; i < locals.Length; i++)
        {
            var parent = skeleton[i].ParentIndex;
            world[i] = parent < 0 ? locals[i] : XForm.Compose(world[parent], locals[i]);
        }

        return world;
    }

    // Left hand to right hand, flattened onto the ground (Y-up)
    private static Vector3 Side(SkeletonModel skeleton, XForm[] world, string left, string right)
    {
        var side = world[skeleton.IndexOf(right)].Pos - world[skeleton.IndexOf(left)].Pos;
        return Vector3.Normalize(new Vector3(side.X, 0f, side.Z));
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
