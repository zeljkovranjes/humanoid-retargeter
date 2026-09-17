using System.Numerics;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Target;

public class SmartPortHelperChainTests
{
    static SkeletonModel Rig(bool driver)
    {
        var bones = new List<BoneDefinition>();
        void Add(string name, string? parent, Vector3 pos) => bones.Add(new(name, parent, new(pos, Quaternion.Identity)));
        Add("pelvis", null, new(0, 0, 40));
        Add("spine_0", "pelvis", new(0, 0, 8));
        Add("spine_1", "spine_0", new(0, 0, 8));
        Add("neck_0", "spine_1", new(0, 0, 5));
        Add("head", "neck_0", new(0, 0, 5));
        foreach (var side in new[] { "L", "R" })
        {
            var y = side == "L" ? 1 : -1;
            Add("clavicle_" + side, "spine_1", new(0, y * 2, 0));
            Add("arm_upper_" + side, "clavicle_" + side, new(0, y * 4, 0));
            Add("arm_lower_" + side, "arm_upper_" + side, new(0, y * 10, 0));
            Add("hand_" + side, "arm_lower_" + side, new(0, y * 9, 0));
            Add("leg_upper_" + side, "pelvis", new(0, y * 4, 0));
            Add("leg_lower_" + side, "leg_upper_" + side, new(0, 0, -18));
            Add("ankle_" + side, "leg_lower_" + side, new(0, 0, -18));
            Add("ball_" + side, "ankle_" + side, new(5, 0, -3));
        }
        if (driver) Add("accessory_driver", "hand_R", Vector3.Zero);
        Add("accessory_socket", driver ? "accessory_driver" : "hand_R", new(2, 0, 0));
        return SkeletonModel.Create(bones);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SkippedAnimatedParentStillDrivesSocket(bool additive)
    {
        var source = Rig(true);
        var target = Rig(false);
        var rig = new SmartPortRig(source, target);
        var pose = source.Bones.Select(b => additive ? XForm.Identity : b.RestLocal).ToArray();
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, .25f);
        pose[source.IndexOf("accessory_driver")].Rot = rotation;
        var output = rig.Transfer(pose, additive);
        var socket = rig.Target.IndexOf("accessory_socket");
        Assert.True(MathQ.AngleBetween(rotation, output[socket].Rot) < .001f);
        var expectedPosition = Vector3.Transform(new Vector3(2, 0, 0), rotation);
        if (additive) expectedPosition -= new Vector3(2, 0, 0);
        Assert.True(Vector3.Distance(expectedPosition, output[socket].Pos) < .001f);
        foreach (var bone in target.Bones)
            Assert.Equal(bone.RestLocal, rig.Target[rig.Target.IndexOf(bone.Name)].RestLocal);
    }
}
