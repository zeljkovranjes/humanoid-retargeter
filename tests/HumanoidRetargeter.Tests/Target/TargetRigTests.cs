using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Target;
using HumanoidRetargeter.Tests.Skeleton;
using Xunit;

namespace HumanoidRetargeter.Tests.Target;

public class TargetRigTests
{
    private static string LoadRigFixture()
        => File.ReadAllText(SkeletonTests.FixturePath("rig_human_male.json"));

    private static TargetRig LoadGeneratedTargetRig()
        => TargetRig.Load(TargetRigGenerator.Generate(LoadRigFixture()));

    // ---------------------------------------------------------------- classifier

    [Theory]
    [InlineData("arm_upper_L_twist0", BoneClass.ConstraintDriven)]
    [InlineData("arm_upper_R_twist1", BoneClass.ConstraintDriven)]
    [InlineData("leg_lower_L_twist1", BoneClass.ConstraintDriven)]
    [InlineData("arm_elbow_helper_L", BoneClass.ConstraintDriven)]
    [InlineData("leg_knee_helper_R", BoneClass.ConstraintDriven)]
    [InlineData("neck_clothing", BoneClass.ConstraintDriven)]
    [InlineData("root_IK", BoneClass.IkBaked)]
    [InlineData("foot_L_IK_target", BoneClass.IkBaked)]
    [InlineData("hand_R_IK_target", BoneClass.IkBaked)]
    [InlineData("hand_L_IK_attach", BoneClass.IkBaked)]
    [InlineData("hand_L_to_R_ikrule", BoneClass.IkBaked)]
    [InlineData("hand_R_to_L_ikrule", BoneClass.IkBaked)]
    [InlineData("aim_matrix_01", BoneClass.IkBaked)]
    [InlineData("aim_matrix_02a", BoneClass.IkBaked)]
    [InlineData("hold_L", BoneClass.IkBaked)]
    [InlineData("hold_R", BoneClass.IkBaked)]
    [InlineData("pelvis", BoneClass.Animated)]
    [InlineData("spine_0", BoneClass.Animated)]
    [InlineData("hand_L", BoneClass.Animated)]
    [InlineData("finger_pinky_meta_R", BoneClass.Animated)]
    [InlineData("ball_L", BoneClass.Animated)]
    public void Classify_FollowsDesignRules(string name, BoneClass expected)
        => Assert.Equal(expected, SboxBoneClassifier.Classify(name));

    [Theory]
    [InlineData("pelvis", BoneRole.Hips)]
    [InlineData("spine_0", BoneRole.Spine0)]
    [InlineData("spine_2", BoneRole.Spine2)]
    [InlineData("neck_0", BoneRole.Neck)]
    [InlineData("head", BoneRole.Head)]
    [InlineData("clavicle_L", BoneRole.ClavicleL)]
    [InlineData("arm_upper_R", BoneRole.UpperArmR)]
    [InlineData("arm_lower_L", BoneRole.LowerArmL)]
    [InlineData("hand_R", BoneRole.HandR)]
    [InlineData("leg_upper_L", BoneRole.UpperLegL)]
    [InlineData("leg_lower_R", BoneRole.LowerLegR)]
    [InlineData("ankle_L", BoneRole.FootL)]
    [InlineData("ball_R", BoneRole.ToeR)]
    [InlineData("finger_thumb_0_L", BoneRole.ThumbProxL)]
    [InlineData("finger_thumb_1_L", BoneRole.ThumbMidL)]
    [InlineData("finger_thumb_2_R", BoneRole.ThumbDistR)]
    [InlineData("finger_index_meta_L", BoneRole.IndexMetaL)]
    [InlineData("finger_index_0_L", BoneRole.IndexProxL)]
    [InlineData("finger_middle_1_R", BoneRole.MiddleMidR)]
    [InlineData("finger_ring_2_L", BoneRole.RingDistL)]
    [InlineData("finger_pinky_meta_R", BoneRole.PinkyMetaR)]
    [InlineData("finger_pinky_0_R", BoneRole.PinkyProxR)]
    public void RoleFor_MapsSboxNames(string name, BoneRole expected)
        => Assert.Equal(expected, SboxBoneClassifier.RoleFor(name));

    [Theory]
    [InlineData("arm_upper_L_twist0")]
    [InlineData("root_IK")]
    [InlineData("hold_L")]
    [InlineData("aim_matrix_01")]
    public void RoleFor_NonAnimatedNames_ReturnsNull(string name)
        => Assert.Null(SboxBoneClassifier.RoleFor(name));

    // ---------------------------------------------------------------- generator

    [Fact]
    public void GeneratedJson_MatchesCommittedAsset()
    {
        var generated = TargetRigGenerator.Generate(LoadRigFixture());

        var assetPath = FindRepoFile(Path.Combine("Assets", "data", "humanoid_retargeter", "target_rig_sbox.json"));
        if (!File.Exists(assetPath))
        {
            // Regenerate-and-diff pattern: first run writes the committed artifact.
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
            File.WriteAllText(assetPath, generated);
        }

        var committed = File.ReadAllText(assetPath);
        Assert.Equal(Normalize(committed), Normalize(generated));
    }

    [Fact]
    public void Generate_IsDeterministic()
    {
        var rigJson = LoadRigFixture();
        Assert.Equal(TargetRigGenerator.Generate(rigJson), TargetRigGenerator.Generate(rigJson));
    }

    // ---------------------------------------------------------------- TargetRig.Load

    [Fact]
    public void Load_RoundTripsGeneratedJson()
    {
        var rig = LoadGeneratedTargetRig();
        var (sourceSkeleton, _) = RigJson.Load(LoadRigFixture());

        Assert.Equal(sourceSkeleton.Count, rig.Skeleton.Count);
        for (var i = 0; i < sourceSkeleton.Count; i++)
        {
            Assert.Equal(sourceSkeleton[i].Name, rig.Skeleton[i].Name);
            Assert.Equal(sourceSkeleton[i].ParentIndex, rig.Skeleton[i].ParentIndex);
            TestUtil.AssertVectorEqual(sourceSkeleton[i].RestLocal.Pos, rig.Skeleton[i].RestLocal.Pos, 1e-3f);
            TestUtil.AssertQuaternionEqual(sourceSkeleton[i].RestLocal.Rot, rig.Skeleton[i].RestLocal.Rot, 1e-5f);
        }
    }

    [Fact]
    public void EveryBoneIsClassified_WithExpectedClassCounts()
    {
        var rig = LoadGeneratedTargetRig();

        Assert.Equal(94, rig.Skeleton.Count);
        Assert.Equal(60, rig.BonesOfClass(BoneClass.Animated).Count());
        Assert.Equal(20, rig.BonesOfClass(BoneClass.ConstraintDriven).Count());
        Assert.Equal(14, rig.BonesOfClass(BoneClass.IkBaked).Count());
    }

    [Fact]
    public void EveryAnimatedBoneHasARole()
    {
        var rig = LoadGeneratedTargetRig();
        foreach (var index in rig.BonesOfClass(BoneClass.Animated))
            Assert.True(rig.RoleOf(index).HasValue,
                $"Animated bone '{rig.Skeleton[index].Name}' has no role.");
    }

    [Fact]
    public void NoNonAnimatedBoneHasARole()
    {
        var rig = LoadGeneratedTargetRig();
        for (var i = 0; i < rig.Skeleton.Count; i++)
        {
            if (rig.ClassOf(i) == BoneClass.Animated)
                continue;
            Assert.False(rig.RoleOf(i).HasValue,
                $"{rig.ClassOf(i)} bone '{rig.Skeleton[i].Name}' must not have a role.");
        }
    }

    [Fact]
    public void RoleLookup_RoundTrips_AndAbsentRolesReturnNull()
    {
        var rig = LoadGeneratedTargetRig();

        var presentRoles = new HashSet<BoneRole>();
        for (var i = 0; i < rig.Skeleton.Count; i++)
        {
            var role = rig.RoleOf(i);
            if (role is null)
                continue;
            Assert.True(presentRoles.Add(role.Value), $"Role {role} assigned twice.");
            Assert.Equal(i, rig.BoneForRole(role.Value));
        }

        // The s&box rig has a 3-bone spine and no thumb metacarpals; those enum values exist
        // for source rigs only and must be absent here.
        var expectedAbsent = new[] { BoneRole.Spine3, BoneRole.Spine4, BoneRole.ThumbMetaL, BoneRole.ThumbMetaR };
        foreach (var role in expectedAbsent)
        {
            Assert.DoesNotContain(role, presentRoles);
            Assert.Null(rig.BoneForRole(role));
        }

        // Every other role is present exactly once.
        foreach (var role in Enum.GetValues<BoneRole>())
        {
            if (expectedAbsent.Contains(role))
                continue;
            Assert.Contains(role, presentRoles);
        }
    }

    [Fact]
    public void Load_ExposesRigName()
    {
        var rig = LoadGeneratedTargetRig();
        Assert.False(string.IsNullOrWhiteSpace(rig.Name));
    }

    // ---------------------------------------------------------------- helpers

    private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd('\n');

    /// <summary>Resolves a repo-relative path by walking up from the test output directory
    /// to the directory containing the .sbproj.</summary>
    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "humanoid-retargeter.sbproj")))
                return Path.Combine(dir.FullName, relativePath);
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root (humanoid-retargeter.sbproj) not found above test directory.");
    }
}
