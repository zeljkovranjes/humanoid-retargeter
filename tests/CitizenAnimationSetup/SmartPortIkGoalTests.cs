using System.Numerics;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using HumanoidRetargeter.Tests.Skeleton;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Target;

public class SmartPortIkGoalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GoalTracksRetargetedEffectorInsteadOfUnrelatedTargetBindOffset(bool animated)
    {
        var original = TargetRig.Load(TargetRigGenerator.Generate(File.ReadAllText(SkeletonTests.FixturePath("rig_human_male.json")))).Skeleton;
        const string goal = "generic_goal";
        const string end = "hand_L";
        var sourceDefinitions = original.Bones.Select(b => new BoneDefinition(b.Name,
            b.ParentIndex < 0 ? null : original[b.ParentIndex].Name, b.RestLocal)).ToList();
        var offset = new XForm(new Vector3(1, 2, 3), Quaternion.CreateFromAxisAngle(Vector3.UnitY, .3f));
        var goalWorld = XForm.Compose(original.RestWorld[original.IndexOf(end)], offset);
        sourceDefinitions.Add(new(goal, "hand_R", XForm.ToLocal(original.RestWorld[original.IndexOf("hand_R")], goalWorld)));
        var source = SkeletonModel.Create(sourceDefinitions);
        var target = SkeletonModel.Create(source.Bones.Select(b => new BoneDefinition(b.Name,
            b.ParentIndex < 0 ? null : source[b.ParentIndex].Name,
            b.Name == goal ? new XForm(new Vector3(100, 50, 25), Quaternion.Identity) : new XForm(b.RestLocal.Pos * .8f, b.RestLocal.Rot))).ToArray());
        var rig = new SmartPortRig(source, target, new Dictionary<string, string> { [goal] = end });
        var pose = source.Bones.Select(b => b.RestLocal).ToArray();
        if (animated) pose[source.IndexOf("arm_lower_L")].Rot *= Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .4f);
        var input = World(source, pose);
        var output = World(rig.Target, rig.Transfer(pose));
        var expected = XForm.ToLocal(input[source.IndexOf(end)], input[source.IndexOf(goal)]);
        expected.Pos *= rig.MotionScale;
        var actual = XForm.ToLocal(output[rig.Target.IndexOf(end)], output[rig.Target.IndexOf(goal)]);
        Assert.True(Vector3.Distance(expected.Pos, actual.Pos) < .001f);
        Assert.True(MathQ.AngleBetween(expected.Rot, actual.Rot) < .001f);
        foreach (var bone in target.Bones) Assert.Equal(bone.RestLocal, rig.Target[rig.Target.IndexOf(bone.Name)].RestLocal);
        var zero = rig.Transfer(Enumerable.Repeat(XForm.Identity, source.Count).ToArray(), true);
        Assert.All(zero, t =>
        {
            Assert.True(t.Pos.Length() < .0001f);
            Assert.True(MathQ.AngleBetween(t.Rot, Quaternion.Identity) < .001f);
        });
    }

    static XForm[] World(SkeletonModel skeleton, XForm[] pose)
    {
        var world = new XForm[skeleton.Count];
        foreach (var bone in skeleton.Bones)
            world[bone.Index] = bone.ParentIndex < 0 ? pose[bone.Index] : XForm.Compose(world[bone.ParentIndex], pose[bone.Index]);
        return world;
    }
}
