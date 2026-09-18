using System.Numerics;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using HumanoidRetargeter.Tests.Skeleton;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Target;

public class SmartPortMissingFingerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingPinkyFollowsRingButAuthoredPinkyIsPreserved(bool sourceHasPinky)
    {
        var target = TargetRig.Load(TargetRigGenerator.Generate(File.ReadAllText(SkeletonTests.FixturePath("rig_human_male.json")))).Skeleton;
        var source = SkeletonModel.Create(target.Bones.Where(b => sourceHasPinky || !b.Name.Contains("pinky"))
            .Select(b => new BoneDefinition(b.Name, b.ParentIndex < 0 ? null : target[b.ParentIndex].Name, b.RestLocal)).ToArray());
        var rig = new SmartPortRig(source, target);
        var pose = source.Bones.Select(b => b.RestLocal).ToArray();
        foreach (var side in new[] { "L", "R" })
        {
            var ring = source.IndexOf("finger_ring_1_" + side);
            pose[ring].Rot *= Quaternion.CreateFromAxisAngle(Vector3.UnitY, .7f);
        }
        var result = rig.Transfer(pose);
        foreach (var side in new[] { "L", "R" })
        {
            var index = rig.Target.IndexOf("finger_pinky_1_" + side);
            var rest = rig.Target[index].RestLocal;
            var curl = MathQ.AngleBetween(rest.Rot, result[index].Rot);
            if (sourceHasPinky) Assert.True(curl < .001f); // Authored straight pinky, despite curled ring.
            else Assert.True(curl > .1f, "A missing source pinky must not leave the target pinky stuck in bind pose.");
            Assert.True(Vector3.Distance(rest.Pos, result[index].Pos) < .001f);
        }
        Assert.All(rig.Transfer(Enumerable.Repeat(XForm.Identity, source.Count).ToArray(), true), delta =>
        {
            Assert.True(delta.Pos.Length() < .0001f);
            Assert.True(MathQ.AngleBetween(delta.Rot, Quaternion.Identity) < .001f);
        });
    }
}
