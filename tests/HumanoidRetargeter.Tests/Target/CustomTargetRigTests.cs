using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Target;
using HumanoidRetargeter.Tests.Mapping;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Target;

/// <summary>
/// Task 9.5.1: arbitrary humanoid TARGET rigs — <see cref="TargetRig.FromSkeleton"/> plus
/// the generic name-pattern <see cref="BoneClassRules"/> (design §1 "Custom targets").
/// </summary>
public class CustomTargetRigTests
{
    private static (SkeletonModel Skeleton, MappingResult Map) DetectAsTarget(SkeletonModel skeleton)
    {
        var detected = ProfileDetector.Detect(skeleton);
        Assert.NotNull(detected);
        return (skeleton, detected.Value.Result);
    }

    // ---------------------------------------------------------------- class rules (synthetic names)

    [Theory]
    // Twist bones across naming conventions.
    [InlineData("arm_upper_L_twist0", BoneClass.ConstraintDriven)] // s&box
    [InlineData("upperarm_twist_01_l", BoneClass.ConstraintDriven)] // UE
    [InlineData("thigh_twist_02_r", BoneClass.ConstraintDriven)] // UE
    [InlineData("CC_Base_L_UpperarmTwist01", BoneClass.ConstraintDriven)] // CC camelCase
    [InlineData("CC_Base_L_RibsTwist", BoneClass.ConstraintDriven)] // CC, no digits
    // Helper / corrective / roll bones.
    [InlineData("arm_elbow_helper_L", BoneClass.ConstraintDriven)]
    [InlineData("forearm_hlp_r", BoneClass.ConstraintDriven)]
    [InlineData("knee_corrective_L", BoneClass.ConstraintDriven)]
    [InlineData("CC_Base_L_ToeBaseShareBone", BoneClass.ConstraintDriven)]
    [InlineData("ForeArmRoll_L", BoneClass.ConstraintDriven)]
    [InlineData("upperarm_roll_01_l", BoneClass.ConstraintDriven)]
    // IK marker bones.
    [InlineData("root_IK", BoneClass.IkBaked)]
    [InlineData("hand_L_IK_target", BoneClass.IkBaked)]
    [InlineData("hand_R_IK_attach", BoneClass.IkBaked)]
    [InlineData("hand_L_to_R_ikrule", BoneClass.IkBaked)]
    [InlineData("aim_matrix_02a", BoneClass.IkBaked)]
    [InlineData("hold_L", BoneClass.IkBaked)]
    [InlineData("ik_foot_root", BoneClass.IkBaked)] // UE ^ik_ prefix
    [InlineData("ik_hand_gun", BoneClass.IkBaked)] // UE ^ik_ prefix
    // Curated s&box literals with no generalizable pattern.
    [InlineData("neck_clothing", BoneClass.ConstraintDriven)] // citizen clothing helper
    // Everything else defaults to Animated; near-miss names must not false-positive.
    [InlineData("pelvis", BoneClass.Animated)]
    [InlineData("finger_ring_2_L", BoneClass.Animated)] // "ring" is not "roll"
    [InlineData("LeftHand", BoneClass.Animated)]
    [InlineData("wrist_l", BoneClass.Animated)] // "wrist" is not "twist"
    [InlineData("holder", BoneClass.Animated)] // not "^hold_"
    public void Rules_Classify_FollowsNamePatterns(string name, BoneClass expected)
        => Assert.Equal(expected, new BoneClassRules().Classify(name));

    // ---------------------------------------------------------------- mixamo as target

    [Fact]
    public void FromSkeleton_MixamoAsTarget_MappedRolesAnimated_HierarchyIntact()
    {
        var (skeleton, map) = DetectAsTarget(MappingFixtures.LoadZombieCrawl());
        Assert.Equal("mixamo", map.ProfileName);

        var rig = TargetRig.FromSkeleton(skeleton, map);

        // The rig wraps the given skeleton unchanged.
        Assert.Same(skeleton, rig.Skeleton);
        Assert.Equal(65, rig.Skeleton.Count);

        // Every mapped role lands on an Animated bone and round-trips through the lookups.
        foreach (var (role, boneIndex) in map.RoleToBone)
        {
            Assert.Equal(BoneClass.Animated, rig.ClassOf(boneIndex));
            Assert.Equal(role, rig.RoleOf(boneIndex));
            Assert.Equal(boneIndex, rig.BoneForRole(role));
        }

        var hips = rig.BoneForRole(BoneRole.Hips);
        Assert.NotNull(hips);
        Assert.Equal("mixamorig1:Hips", rig.Skeleton[hips.Value].Name);

        // Mixamo has no twist/helper/IK bones — nothing may be misclassified away
        // from Animated (the unmapped *_End leaf bones included).
        Assert.Equal(skeleton.Count, rig.BonesOfClass(BoneClass.Animated).Count());
    }

    // ---------------------------------------------------------------- actorcore as target

    [Fact]
    public void FromSkeleton_ActorCoreAsTarget_TwistAndShareBonesConstraintDriven()
    {
        var (skeleton, map) = DetectAsTarget(MappingFixtures.LoadActorCore());
        Assert.Equal("actorcore_cc", map.ProfileName);

        var rig = TargetRig.FromSkeleton(skeleton, map);

        foreach (var name in new[]
        {
            "CC_Base_L_UpperarmTwist01", "CC_Base_R_UpperarmTwist02",
            "CC_Base_L_ForearmTwist01", "CC_Base_L_ThighTwist01", "CC_Base_R_CalfTwist02",
            "CC_Base_L_RibsTwist", "CC_Base_NeckTwist02", // unmapped neck helper
            "CC_Base_L_ToeBaseShareBone", "CC_Base_L_KneeShareBone", "CC_Base_R_ElbowShareBone",
        })
        {
            var index = skeleton.IndexOf(name);
            Assert.True(index >= 0, $"Fixture bone '{name}' not found.");
            Assert.Equal(BoneClass.ConstraintDriven, rig.ClassOf(index));
            Assert.Null(rig.RoleOf(index));
        }

        // Every mapped role is Animated...
        foreach (var (role, boneIndex) in map.RoleToBone)
        {
            Assert.Equal(BoneClass.Animated, rig.ClassOf(boneIndex));
            Assert.Equal(boneIndex, rig.BoneForRole(role));
        }

        // ...including CC_Base_NeckTwist01, which matches the twist pattern by name but
        // IS the neck bone — the mapped role wins over the twist-like name.
        var neck = rig.BoneForRole(BoneRole.Neck);
        Assert.NotNull(neck);
        Assert.Equal("CC_Base_NeckTwist01", rig.Skeleton[neck.Value].Name);
        Assert.Equal(BoneClass.Animated, rig.ClassOf(neck.Value));
    }

    // ---------------------------------------------------------------- sbox round-trip sanity

    /// <summary>Builds a <see cref="TargetRig"/> from a rig JSON the way custom-target
    /// detection would (roles from the curated classifier) and diffs every bone class
    /// against <see cref="SboxBoneClassifier"/>.</summary>
    private static List<string> ClassMismatchesAgainstSboxClassifier(string rigJsonFixture)
    {
        var (skeleton, _) = RigJson.Load(
            File.ReadAllText(MappingFixtures.FixturePath(rigJsonFixture)));

        var map = new MappingResult("sbox", MappingSource.Preset);
        foreach (var bone in skeleton.Bones)
        {
            if (SboxBoneClassifier.RoleFor(bone.Name) is { } role)
                map.RoleToBone[role] = bone.Index;
        }

        var rig = TargetRig.FromSkeleton(skeleton, map);

        var mismatches = new List<string>();
        for (var i = 0; i < skeleton.Count; i++)
        {
            var expected = SboxBoneClassifier.Classify(skeleton[i].Name);
            var actual = rig.ClassOf(i);
            if (actual != expected)
                mismatches.Add($"{skeleton[i].Name}: rules={actual}, sbox={expected}");
        }
        return mismatches;
    }

    [Fact]
    public void FromSkeleton_CitizenRig_AgreesWithSboxBoneClassifier()
    {
        // The 95-bone citizen rig adds neck_clothing (ConstraintDriven by curated literal);
        // the generic rules must classify the whole rig identically.
        var mismatches = ClassMismatchesAgainstSboxClassifier("rig_citizen.json");
        Assert.True(mismatches.Count == 0,
            $"Expected full agreement on the citizen rig; mismatches: {string.Join("; ", mismatches)}");
    }

    [Fact]
    public void FromSkeleton_SboxRig_AgreesWithSboxBoneClassifier()
    {
        var (skeleton, _) = RigJson.Load(
            File.ReadAllText(MappingFixtures.FixturePath("rig_human_male.json")));

        // The known mapping for the shipped rig (what detection would have produced).
        var map = new MappingResult("sbox_human_male", MappingSource.Preset);
        foreach (var bone in skeleton.Bones)
        {
            if (SboxBoneClassifier.RoleFor(bone.Name) is { } role)
                map.RoleToBone[role] = bone.Index;
        }

        var rig = TargetRig.FromSkeleton(skeleton, map);

        var mismatches = new List<string>();
        for (var i = 0; i < skeleton.Count; i++)
        {
            var expected = SboxBoneClassifier.Classify(skeleton[i].Name);
            var actual = rig.ClassOf(i);
            if (actual != expected)
                mismatches.Add($"{skeleton[i].Name}: rules={actual}, sbox={expected}");
        }

        // The generic patterns were designed from the sbox names; require >= 90% agreement
        // (and currently expect 100%).
        var agreement = (skeleton.Count - mismatches.Count) / (float)skeleton.Count;
        Assert.True(agreement >= 0.9f,
            $"Only {agreement:P1} of {skeleton.Count} bones agree with SboxBoneClassifier. "
            + $"Mismatches: {string.Join("; ", mismatches)}");
        Assert.True(mismatches.Count == 0,
            $"Expected full agreement on the sbox rig; mismatches: {string.Join("; ", mismatches)}");
    }

    // ---------------------------------------------------------------- validation

    [Fact]
    public void FromSkeleton_RejectsOutOfRangeAndDuplicateBoneAssignments()
    {
        var skeleton = MappingFixtures.LoadZombieCrawl();

        var outOfRange = new MappingResult("broken", MappingSource.Manual);
        outOfRange.RoleToBone[BoneRole.Hips] = skeleton.Count;
        Assert.Throws<ArgumentException>(() => TargetRig.FromSkeleton(skeleton, outOfRange));

        var duplicate = new MappingResult("broken", MappingSource.Manual);
        duplicate.RoleToBone[BoneRole.Hips] = 0;
        duplicate.RoleToBone[BoneRole.Spine0] = 0;
        Assert.Throws<ArgumentException>(() => TargetRig.FromSkeleton(skeleton, duplicate));
    }

    [Fact]
    public void SboxDefault_ParsesCommittedTargetRigJson()
    {
        var json = TargetRigGenerator.Generate(
            File.ReadAllText(MappingFixtures.FixturePath("rig_human_male.json")));
        var rig = TargetRig.SboxDefault(json);
        Assert.Equal("sbox_human_male", rig.Name);
        Assert.NotNull(rig.BoneForRole(BoneRole.Hips));
    }
}
