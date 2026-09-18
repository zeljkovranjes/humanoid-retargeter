using System.Numerics;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using HumanoidRetargeter.Tests.Skeleton;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Target;

public class SmartPortHandSocketTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CopiedSocketFitsTheTargetHandNotItsLegProportions(int helperDepth)
    {
        var original = TargetRig.Load(TargetRigGenerator.Generate(File.ReadAllText(SkeletonTests.FixturePath("rig_human_male.json")))).Skeleton;
        var names = new[] { "finger_index_0_R", "finger_middle_0_R", "finger_ring_0_R" };
        Vector3 Center(SkeletonModel sk, XForm[] world) => names.Select(n => world[sk.IndexOf(n)].Pos).Aggregate(Vector3.Zero, (a,b) => a+b) / names.Length;
        const string socket = "new_hand_socket";
        var bind = original.RestWorld.ToArray();
        var hand = original.IndexOf("hand_R");
        var definitions = original.Bones.Select(b => new BoneDefinition(b.Name,
            b.ParentIndex < 0 ? null : original[b.ParentIndex].Name, b.RestLocal)).ToList();
        definitions.Add(new(socket, "hand_R", new XForm(bind[hand].Inverse().TransformPoint(Center(original, bind)), Quaternion.Identity)));
        var attachment = socket;
        for (var i = 0; i < helperDepth; i++)
        {
            var child = "nested_socket_" + i;
            definitions.Add(new(child, attachment, XForm.Identity));
            attachment = child;
        }
        var source = SkeletonModel.Create(definitions);
        var target = SkeletonModel.Create(original.Bones.Select(b => new BoneDefinition(b.Name,
            b.ParentIndex < 0 ? null : original[b.ParentIndex].Name,
            new XForm(b.RestLocal.Pos * (b.Name.StartsWith("finger_") ? .5f : 1f), b.RestLocal.Rot))).ToArray());
        var rig = new SmartPortRig(source, target, attachmentBones: new[] { attachment, socket });
        var pose = source.Bones.Select(b => b.RestLocal).ToArray();
        var result = rig.Transfer(pose);
        var world = new Pose(result).ToWorld(rig.Target);
        var unfitted = new SmartPortRig(source, target);
        var before = new Pose(unfitted.Transfer(pose)).ToWorld(unfitted.Target);
        Assert.True(Vector3.Distance(Center(unfitted.Target, before), before[unfitted.Target.IndexOf(socket)].Pos) > .1f);
        Assert.True(Vector3.Distance(Center(rig.Target, world), world[rig.Target.IndexOf(socket)].Pos) < .001f,
            $"Expected {Center(rig.Target, world)}, got {world[rig.Target.IndexOf(socket)].Pos}");
        Assert.True(Vector3.Distance(Center(rig.Target, world), world[rig.Target.IndexOf(attachment)].Pos) < .001f);
        foreach (var bone in target.Bones)
            Assert.Equal(bone.RestLocal, rig.Target[rig.Target.IndexOf(bone.Name)].RestLocal);
        Assert.All(rig.Transfer(Enumerable.Repeat(XForm.Identity, source.Count).ToArray(), true), delta =>
        {
            Assert.True(delta.Pos.Length() < .0001f);
            Assert.True(MathQ.AngleBetween(delta.Rot, Quaternion.Identity) < .001f);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingFittedSocketIsNotReplacedByHandGeometry(bool nestedSource)
    {
        var original = TargetRig.Load(TargetRigGenerator.Generate(File.ReadAllText(SkeletonTests.FixturePath("rig_human_male.json")))).Skeleton;
        var definitions = original.Bones.Select(b => new BoneDefinition(b.Name,
            nestedSource && b.Name == "hold_R" ? "weapon_pivot" : b.ParentIndex < 0 ? null : original[b.ParentIndex].Name, b.RestLocal)).ToList();
        if (nestedSource) definitions.Add(new("weapon_pivot", "hand_R", XForm.Identity));
        var source = SkeletonModel.Create(definitions);
        var target = SkeletonModel.Create(original.Bones.Select(b => new BoneDefinition(b.Name,
            b.ParentIndex < 0 ? null : original[b.ParentIndex].Name,
            new XForm(b.RestLocal.Pos * (b.Name.StartsWith("finger_") ? .5f : 1f)
                + (b.Name == "hold_R" ? new Vector3(.2f, .1f, .3f) : Vector3.Zero), b.RestLocal.Rot))).ToArray());
        var fitted = new SmartPortRig(source, target, attachmentBones: new[] { "hold_R" });
        var ordinary = new SmartPortRig(source, target);
        var pose = source.Bones.Select(b => b.RestLocal).ToArray();
        Assert.Equal(ordinary.Transfer(pose), fitted.Transfer(pose));
    }
}
