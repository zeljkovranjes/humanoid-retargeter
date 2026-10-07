using System.Numerics;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Target;
using HumanoidRetargeter.Tests.Skeleton;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

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

    /// <summary>A target with a half-size hand whose hold_R sits outside the hand entirely.</summary>
    static (SkeletonModel Source, SkeletonModel Target) OffHandSocket()
    {
        var original = TargetRig.Load(TargetRigGenerator.Generate(File.ReadAllText(SkeletonTests.FixturePath("rig_human_male.json")))).Skeleton;
        var small = SkeletonModel.Create(original.Bones.Select(b => new BoneDefinition(b.Name,
            b.ParentIndex < 0 ? null : original[b.ParentIndex].Name,
            new XForm(b.RestLocal.Pos * (b.Name.StartsWith("finger_") ? .5f : 1f), b.RestLocal.Rot))).ToArray());
        var hand = small.IndexOf("hand_R");
        var reach = small.Bones.Where(b => b.Name.StartsWith("finger_") && b.Name.EndsWith("_R"))
            .Max(b => Vector3.Distance(small.RestWorld[b.Index].Pos, small.RestWorld[hand].Pos));
        // Well past the fingertips: a weapon on it would hang in the air.
        var place = small.RestWorld[hand].Pos + Vector3.UnitZ * reach * 2.2f;
        var target = SkeletonModel.Create(small.Bones.Select(b => new BoneDefinition(b.Name, b.ParentIndex < 0 ? null : small[b.ParentIndex].Name,
            b.Name == "hold_R" ? new XForm(small.RestWorld[hand].Inverse().TransformPoint(place), b.RestLocal.Rot) : b.RestLocal)).ToArray());
        return (original, target);
    }

    static Vector3 Knuckles(SkeletonModel sk, XForm[] world) => new[] { "finger_index_0_R", "finger_middle_0_R", "finger_ring_0_R" }
        .Select(n => world[sk.IndexOf(n)].Pos).Aggregate(Vector3.Zero, (a, b) => a + b) / 3f;

    [Fact]
    public void ExistingSocketOutsideTheHandIsFittedToTheKnuckles()
    {
        var (source, target) = OffHandSocket();
        var rig = new SmartPortRig(source, target, attachmentBones: new[] { "hold_R" });
        var unfitted = new SmartPortRig(source, target);
        var pose = source.Bones.Select(b => b.RestLocal).ToArray();
        var world = new Pose(rig.Transfer(pose)).ToWorld(rig.Target);
        var before = new Pose(unfitted.Transfer(pose)).ToWorld(unfitted.Target);
        var socket = rig.Target.IndexOf("hold_R");
        var sourceGrip = source.RestWorld[source.IndexOf("hold_R")].Pos - Knuckles(source, source.RestWorld.ToArray());
        // Where the source's grip lands on this hand: the knuckle centre plus the source offset.
        var expected = Knuckles(rig.Target, world) + sourceGrip * rig.MotionScale;
        Assert.True(Vector3.Distance(before[socket].Pos, expected) > 1f, "the socket must start off the grip");
        Assert.True(Vector3.Distance(world[socket].Pos, expected) < .001f, $"Expected {expected}, got {world[socket].Pos}");
        // Rotation stays as animated; the bind and every other bone are untouched.
        Assert.True(MathQ.AngleBetween(world[socket].Rot, before[socket].Rot) < .001f);
        foreach (var bone in target.Bones)
            Assert.Equal(bone.RestLocal, rig.Target[rig.Target.IndexOf(bone.Name)].RestLocal);
        var result = rig.Transfer(pose);
        var plain = unfitted.Transfer(pose);
        for (var i = 0; i < result.Length; i++)
            if (i != socket) Assert.Equal(plain[i], result[i]);
        // Additive clips: a zero delta stays zero.
        Assert.All(rig.Transfer(Enumerable.Repeat(XForm.Identity, source.Count).ToArray(), true), delta =>
        {
            Assert.True(delta.Pos.Length() < .0001f);
            Assert.True(MathQ.AngleBetween(delta.Rot, Quaternion.Identity) < .001f);
        });
    }

    [Fact]
    public void FittedExistingSocketFollowsTheGripWhileTheHandAnimates()
    {
        var (source, target) = OffHandSocket();
        var rig = new SmartPortRig(source, target, attachmentBones: new[] { "hold_R" });
        // Bend the wrist: the grip moves with the hand, the socket must stay on it.
        var hand = source.IndexOf("hand_R");
        var pose = source.Bones.Select(b => b.RestLocal).ToArray();
        pose[hand] = new XForm(pose[hand].Pos, Quaternion.Normalize(pose[hand].Rot * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .6f)));
        var sourceWorld = new Pose(pose).ToWorld(source);
        var sourceGrip = sourceWorld[source.IndexOf("hold_R")].Pos - Knuckles(source, sourceWorld);
        var world = new Pose(rig.Transfer(pose)).ToWorld(rig.Target);
        var expected = Knuckles(rig.Target, world) + sourceGrip * rig.MotionScale;
        Assert.True(Vector3.Distance(world[rig.Target.IndexOf("hold_R")].Pos, expected) < .001f,
            $"Expected {expected}, got {world[rig.Target.IndexOf("hold_R")].Pos}");
    }
}
