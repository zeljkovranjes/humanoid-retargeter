using System.Numerics;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using HumanoidRetargeter.Tests.Skeleton;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Target;

public class SmartPortAdditiveIkTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecoilDoesNotInventGoalTranslationFromDifferentArmBindPoses(bool authoredGoalMotion)
    {
        var original = TargetRig.Load(TargetRigGenerator.Generate(File.ReadAllText(SkeletonTests.FixturePath("rig_human_male.json")))).Skeleton;
        const string goal = "support_goal";
        var definitions = original.Bones.Select(b => new BoneDefinition(b.Name,
            b.ParentIndex < 0 ? null : original[b.ParentIndex].Name, b.RestLocal)).ToList();
        definitions.Add(new(goal, "hand_R", XForm.ToLocal(original.RestWorld[original.IndexOf("hand_R")],
            original.RestWorld[original.IndexOf("hand_L")])));
        var source = SkeletonModel.Create(definitions);
        var target = SkeletonModel.Create(source.Bones.Select(b => new BoneDefinition(b.Name,
            b.ParentIndex < 0 ? null : source[b.ParentIndex].Name,
            new XForm(b.RestLocal.Pos * .8f, b.RestLocal.Rot *
                (b.Name.StartsWith("arm_upper_") ? Quaternion.CreateFromAxisAngle(Vector3.UnitY, .6f) : Quaternion.Identity)))).ToArray());
        var rig = new SmartPortRig(source, target, new Dictionary<string, string> { [goal] = "hand_L" });
        var ordinary = new SmartPortRig(source, target);
        var pose = Enumerable.Repeat(XForm.Identity, source.Count).ToArray();
        pose[source.IndexOf("arm_upper_R")].Rot = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .2f);
        pose[source.IndexOf("arm_lower_L")].Rot = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -.3f);
        pose[source.IndexOf(goal)] = new XForm(authoredGoalMotion ? new Vector3(.2f, -.1f, .3f) : Vector3.Zero,
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, .1f));
        var actual = rig.Transfer(pose, true)[rig.Target.IndexOf(goal)];
        var expected = ordinary.Transfer(pose, true)[ordinary.Target.IndexOf(goal)];
        // Additive goals are local deltas on the already fitted grip, not absolute
        // effector positions computed against a reconstructed rest-pose animation.
        Assert.True(Vector3.Distance(expected.Pos, actual.Pos) < .0001f, $"Expected {expected.Pos}, got {actual.Pos}");
        Assert.True(MathQ.AngleBetween(expected.Rot, actual.Rot) < .001f);
        if (!authoredGoalMotion) Assert.True(actual.Pos.Length() < .0001f);
        else Assert.True(actual.Pos.Length() > .1f); // Preserve authored motion; do not freeze the goal.
    }
}
