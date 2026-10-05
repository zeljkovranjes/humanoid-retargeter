using System.Numerics;
using HumanoidRetargeter.Mapping;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Target;

/// <summary>Roles a caller assigns for rigs the mapper cannot read (a creature whose head is replaced by another part).</summary>
public class SmartPortRoleTests
{
    /// <summary>A Citizen-named humanoid; <paramref name="headless"/> puts a "crab" with legs on the chest instead of a neck and head.</summary>
    static SkeletonModel Rig(bool headless)
    {
        var bones = new List<BoneDefinition>();
        void Bone(string name, string? parent, Vector3 position) => bones.Add(new(name, parent, new XForm(position, Quaternion.Identity)));
        Bone("pelvis", null, new(0, 0, 40));
        Bone("spine_0", "pelvis", new(0, 0, 6));
        Bone("spine_1", "spine_0", new(0, 0, 6));
        Bone("spine_2", "spine_1", new(0, 0, 6));
        if (headless)
        {
            Bone("crab_body", "spine_2", new(1, 0, 7));
            Bone("crab_leg_a", "crab_body", new(2, 2, 0));
            Bone("crab_leg_b", "crab_body", new(2, -2, 0));
        }
        else
        {
            Bone("neck_0", "spine_2", new(0, 0, 3));
            Bone("head", "neck_0", new(0, 0, 5));
        }
        foreach (var side in new[] { "L", "R" })
        {
            var sign = side == "L" ? 1 : -1;
            Bone("clavicle_" + side, "spine_2", new(0, sign * 2, 1));
            Bone("arm_upper_" + side, "clavicle_" + side, new(0, sign * 4, 0));
            Bone("arm_lower_" + side, "arm_upper_" + side, new(0, sign * 10, 0));
            Bone("hand_" + side, "arm_lower_" + side, new(0, sign * 9, 0));
            Bone("leg_upper_" + side, "pelvis", new(0, sign * 4, 0));
            Bone("leg_lower_" + side, "leg_upper_" + side, new(0, 0, -18));
            Bone("ankle_" + side, "leg_lower_" + side, new(0, 0, -18));
            Bone("ball_" + side, "ankle_" + side, new(5, 0, -3));
        }
        return SkeletonModel.Create(bones);
    }

    [Fact]
    public void AnAssignedRoleStandsInForOneTheMapperCannotFind()
    {
        var source = Rig(headless: false);
        var target = Rig(headless: true);
        var error = Assert.Throws<ArgumentException>(() => new SmartPortRig(source, target));
        Assert.Contains("Head", error.Message);

        var plan = new SmartPortRig(source, target, targetRoles: new Dictionary<BoneRole, string> { [BoneRole.Head] = "crab_body" });
        Assert.Equal("crab_body", plan.BoneNames["head"]);
        // the crab keeps its own bind; nothing of the target's skeleton is replaced
        foreach (var bone in target.Bones)
            Assert.Equal(bone.RestLocal, plan.Target[plan.Target.IndexOf(bone.Name)].RestLocal);
        var pose = plan.Transfer(source.Bones.Select(b => b.RestLocal).ToArray());
        Assert.All(pose, x => Assert.True(float.IsFinite(x.Pos.LengthSquared()) && float.IsFinite(x.Rot.LengthSquared())));
    }

    [Fact]
    public void AnAssignedRoleMustNameATargetBone()
        => Assert.Throws<ArgumentException>(() => new SmartPortRig(Rig(false), Rig(true), targetRoles: new Dictionary<BoneRole, string> { [BoneRole.Head] = "missing" }));
}
