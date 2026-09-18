using System.Numerics;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using HumanoidRetargeter.Tests.Skeleton;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Target;

public class SmartPortCopiedHelperTests
{
    [Fact]
    public void CopiedGraphHelperKeepsAnimatedFrameWhenParentAxesDiffer()
    {
        var original = TargetRig.Load(TargetRigGenerator.Generate(File.ReadAllText(SkeletonTests.FixturePath("rig_human_male.json")))).Skeleton;
        var definitions = original.Bones.Select(b => new BoneDefinition(b.Name,
            b.ParentIndex < 0 ? null : original[b.ParentIndex].Name, b.RestLocal)).ToList();
        const string helper = "new_graph_frame";
        definitions.Add(new(helper, "hand_R", new XForm(new Vector3(1, 2, 3), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .4f))));
        var source = SkeletonModel.Create(definitions);
        var target = SkeletonModel.Create(original.Bones.Select(b => new BoneDefinition(b.Name,
            b.ParentIndex < 0 ? null : original[b.ParentIndex].Name,
            new XForm(b.RestLocal.Pos * .8f, b.RestLocal.Rot * (b.Name == "hand_R"
                ? Quaternion.CreateFromAxisAngle(Vector3.UnitX, .7f) : Quaternion.Identity)))).ToArray());
        var rig = new SmartPortRig(source, target);
        var pose = source.Bones.Select(b => b.RestLocal).ToArray();
        pose[source.IndexOf("arm_lower_R")].Rot *= Quaternion.CreateFromAxisAngle(Vector3.UnitY, .5f);
        pose[source.IndexOf(helper)].Pos += new Vector3(.1f, 0, .2f);
        var input = World(source, pose);
        var output = World(rig.Target, rig.Transfer(pose));
        var s = source.IndexOf(helper); var t = rig.Target.IndexOf(helper);
        var expectedOffset = (input[s].Pos - input[source[s].ParentIndex].Pos) * rig.MotionScale;
        Assert.True(Vector3.Distance(expectedOffset, output[t].Pos - output[rig.Target[t].ParentIndex].Pos) < .001f);
        Assert.True(MathQ.AngleBetween(input[s].Rot, output[t].Rot) < .001f);
        foreach (var bone in target.Bones) Assert.Equal(bone.RestLocal, rig.Target[rig.Target.IndexOf(bone.Name)].RestLocal);
        Assert.All(rig.Transfer(Enumerable.Repeat(XForm.Identity, source.Count).ToArray(), true), delta =>
        {
            Assert.True(delta.Pos.Length() < .0001f);
            Assert.True(MathQ.AngleBetween(delta.Rot, Quaternion.Identity) < .001f);
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
