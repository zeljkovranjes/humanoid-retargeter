using System.Numerics;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using HumanoidRetargeter.Tests.Skeleton;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Target;

public class SmartPortCopiedGripTests
{
    [Fact]
    public void NewCrossHandGoalKeepsSourceGripSeparationOnDifferentArmRestPose()
    {
        var original = TargetRig.Load(TargetRigGenerator.Generate(File.ReadAllText(SkeletonTests.FixturePath("rig_human_male.json")))).Skeleton;
        const string goal = "new_support_goal";
        var definitions = original.Bones.Select(b => new BoneDefinition(b.Name,
            b.ParentIndex < 0 ? null : original[b.ParentIndex].Name, b.RestLocal)).ToList();
        definitions.Add(new(goal, "hand_R", XForm.ToLocal(original.RestWorld[original.IndexOf("hand_R")],
            original.RestWorld[original.IndexOf("hand_L")])));
        var source = SkeletonModel.Create(definitions);
        var target = SkeletonModel.Create(original.Bones.Select(b => new BoneDefinition(b.Name,
            b.ParentIndex < 0 ? null : original[b.ParentIndex].Name,
            new XForm(b.RestLocal.Pos * (b.Name == "arm_lower_L" ? .55f : .8f), b.RestLocal.Rot * (b.Name == "arm_upper_L"
                ? Quaternion.CreateFromAxisAngle(Vector3.UnitY, .6f) : Quaternion.Identity)))).ToArray());
        var rig = new SmartPortRig(source, target, new Dictionary<string, string> { [goal] = "hand_L" });
        var pose = source.Bones.Select(b => b.RestLocal).ToArray();
        pose[source.IndexOf("arm_lower_R")].Rot *= Quaternion.CreateFromAxisAngle(Vector3.UnitY, .5f);
        var input = new Pose(pose).ToWorld(source);
        var output = new Pose(rig.Transfer(pose)).ToWorld(rig.Target);
        var expected = (input[source.IndexOf(goal)].Pos - input[source.IndexOf("hand_R")].Pos) * rig.MotionScale;
        var actual = output[rig.Target.IndexOf(goal)].Pos - output[rig.Target.IndexOf("hand_R")].Pos;
        Assert.True(Vector3.Distance(expected, actual) < .001f, $"Expected relative grip {expected}, got {actual}");
    }
}
