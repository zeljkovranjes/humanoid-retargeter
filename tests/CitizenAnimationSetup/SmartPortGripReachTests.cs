using System.Numerics;
using HumanoidRetargeter.Cleanup;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using HumanoidRetargeter.Tests.Skeleton;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Target;

public class SmartPortGripReachTests
{
    [Fact]
    public void SharedGripFitsShorterArmsWithoutStretching()
    {
        var original = TargetRig.Load(TargetRigGenerator.Generate(File.ReadAllText(SkeletonTests.FixturePath("rig_human_male.json")))).Skeleton;
        var definitions = original.Bones.Select(b => new BoneDefinition(b.Name,
            b.ParentIndex < 0 ? null : original[b.ParentIndex].Name, b.RestLocal)).ToList();
        foreach (var side in new[] { "L", "R" }) definitions.Add(new("grip_" + side, "hand_" + side, XForm.Identity));
        definitions.Add(new("support_goal", "hand_R", XForm.Identity));
        var source = SkeletonModel.Create(definitions);
        var target = SkeletonModel.Create(original.Bones.Select(b => new BoneDefinition(b.Name,
            b.ParentIndex < 0 ? null : original[b.ParentIndex].Name,
            new XForm(b.RestLocal.Pos * (b.Name is "arm_lower_L" or "hand_L" ? .55f : 1), b.RestLocal.Rot))).ToArray());
        var pose = source.Bones.Select(b => b.RestLocal).ToArray();
        var left = source.IndexOf("arm_upper_L"); var right = source.IndexOf("arm_upper_R");
        var center = (source.RestWorld[left].Pos + source.RestWorld[right].Pos) * .5f;
        var up = Vector3.Normalize(source.RestWorld[source.IndexOf("head")].Pos - source.RestWorld[source.IndexOf("pelvis")].Pos);
        var lateral = Vector3.Normalize(source.RestWorld[right].Pos - source.RestWorld[left].Pos);
        var forward = Vector3.Normalize(Vector3.Cross(up, lateral));
        var reach = source[source.IndexOf("arm_lower_R")].RestLocal.Pos.Length() + source[source.IndexOf("hand_R")].RestLocal.Pos.Length();
        foreach (var side in new[] { "L", "R" })
        {
            var chain = new LimbChain { Upper = source.IndexOf("arm_upper_" + side), Lower = source.IndexOf("arm_lower_" + side), End = source.IndexOf("hand_" + side) };
            var goal = center + forward * reach * (side == "L" ? .75f : .5f) + lateral * (side == "L" ? -4 : 4) - up * reach * .1f;
            EffectorIk.ApplyGoals(new() { pose }, source, chain, new[] { goal }, lateral, soften: 0);
        }
        var input = new Pose(pose).ToWorld(source);
        pose[source.IndexOf("support_goal")] = XForm.ToLocal(input[source.IndexOf("hand_R")], input[source.IndexOf("hand_L")]);
        var targets = new Dictionary<string, string> { ["support_goal"] = "hand_L" };
        var rig = new SmartPortRig(source, target, targets, new[] { "grip_L", "grip_R" });
        var result = rig.Transfer(pose);
        var world = new Pose(result).ToWorld(rig.Target);
        var error = Vector3.Distance(world[rig.Target.IndexOf("hand_L")].Pos, world[rig.Target.IndexOf("support_goal")].Pos);
        Assert.True(error < .01f, $"Support hand missed its goal by {error}");
        foreach (var side in new[] { "L", "R" })
        foreach (var name in new[] { "arm_upper_", "arm_lower_", "hand_" })
        {
            var index = rig.Target.IndexOf(name + side);
            Assert.True(Vector3.Distance(rig.Target[index].RestLocal.Pos, result[index].Pos) < .0001f);
        }
        var unfitted = new SmartPortRig(source, target, targets);
        var before = new Pose(unfitted.Transfer(pose)).ToWorld(unfitted.Target);
        Assert.True(Vector3.Distance(before[unfitted.Target.IndexOf("hand_L")].Pos, before[unfitted.Target.IndexOf("support_goal")].Pos) > .1f);
        Assert.All(rig.Transfer(Enumerable.Repeat(XForm.Identity, source.Count).ToArray(), true), delta =>
        {
            Assert.True(delta.Pos.Length() < .0001f);
            Assert.True(MathQ.AngleBetween(delta.Rot, Quaternion.Identity) < .001f);
        });
    }
}
