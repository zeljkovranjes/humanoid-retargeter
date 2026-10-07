using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Target;
using HumanoidRetargeter.Tests.Skeleton;
using Xunit;

namespace HumanoidRetargeter.Tests.Target;

/// <summary>
/// The classic (4-finger) s&amp;box citizen as a built-in target: regenerate-and-diff against
/// the committed <c>Assets/humanoid_retargeter/target_rig_sbox_citizen.json</c>, plus the
/// rig's structural invariants — same role coverage as the human rig MINUS the pinky roles
/// (the citizen has no pinky bones), face/eye/ear bones constraint-driven and role-less.
/// </summary>
public class CitizenTargetRigTests
{
    private static string LoadCitizenFixture()
        => File.ReadAllText(SkeletonTests.FixturePath("rig_citizen.json"));

    private static string GenerateCitizenJson()
        => TargetRigGenerator.Generate(LoadCitizenFixture(),
            TargetRigGenerator.CitizenName, TargetRigGenerator.CitizenDescription);

    private static TargetRig LoadGeneratedTargetRig() => TargetRig.Load(GenerateCitizenJson());

    private static TargetRig LoadGeneratedHumanRig()
        => TargetRig.Load(TargetRigGenerator.Generate(
            File.ReadAllText(SkeletonTests.FixturePath("rig_human_male.json"))));

    // ---------------------------------------------------------------- generator

    [Fact]
    public void GeneratedJson_MatchesCommittedAsset()
    {
        var generated = GenerateCitizenJson();

        var assetPath = FindRepoFile(Path.Combine("Assets", "data", "humanoid_retargeter", "target_rig_sbox_citizen.json"));
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
        var rigJson = LoadCitizenFixture();
        Assert.Equal(
            TargetRigGenerator.Generate(rigJson, TargetRigGenerator.CitizenName, TargetRigGenerator.CitizenDescription),
            TargetRigGenerator.Generate(rigJson, TargetRigGenerator.CitizenName, TargetRigGenerator.CitizenDescription));
    }

    [Fact]
    public void Load_ExposesCitizenRigName()
        => Assert.Equal("sbox_citizen", LoadGeneratedTargetRig().Name);

    // ---------------------------------------------------------------- classes

    [Fact]
    public void EveryBoneIsClassified_WithExpectedClassCounts()
    {
        var rig = LoadGeneratedTargetRig();

        Assert.Equal(95, rig.Skeleton.Count);
        // 52 Animated = the human rig's 60 minus the 8 pinky bones the citizen lacks.
        Assert.Equal(52, rig.BonesOfClass(BoneClass.Animated).Count());
        // 31 ConstraintDriven = 16 twists + 6 helpers (elbow/knee/glute) + neck_clothing
        // + 8 face bones (eye_L/R, ear_L/R, face_lid_{upper,lower}_{L,R}).
        Assert.Equal(31, rig.BonesOfClass(BoneClass.ConstraintDriven).Count());
        Assert.Equal(12, rig.BonesOfClass(BoneClass.IkBaked).Count());
    }

    [Theory]
    [InlineData("eye_L")]
    [InlineData("eye_R")]
    [InlineData("ear_L")]
    [InlineData("ear_R")]
    [InlineData("face_lid_upper_L")]
    [InlineData("face_lid_upper_R")]
    [InlineData("face_lid_lower_L")]
    [InlineData("face_lid_lower_R")]
    public void FaceBones_AreConstraintDriven_AndRoleLess(string name)
    {
        // The engine drives these at runtime (eye look-at, blinking); the retargeter must
        // neither solve nor pin them, exactly like the twist/helper bones.
        Assert.Equal(BoneClass.ConstraintDriven, SboxBoneClassifier.Classify(name));
        Assert.Null(SboxBoneClassifier.RoleFor(name));

        var rig = LoadGeneratedTargetRig();
        var index = IndexOf(rig, name);
        Assert.Equal(BoneClass.ConstraintDriven, rig.ClassOf(index));
        Assert.Null(rig.RoleOf(index));
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

    // ---------------------------------------------------------------- role coverage vs human

    [Fact]
    public void RoleCoverage_IsExactlyHumanRolesMinusPinky()
    {
        var citizenRoles = PresentRoles(LoadGeneratedTargetRig());
        var humanRoles = PresentRoles(LoadGeneratedHumanRig());

        var pinkyRoles = Enum.GetValues<BoneRole>()
            .Where(r => r.ToString().StartsWith("Pinky", StringComparison.Ordinal))
            .ToHashSet();
        Assert.Equal(8, pinkyRoles.Count); // Meta/Prox/Mid/Dist x L/R

        var expected = humanRoles.Except(pinkyRoles).ToHashSet();
        Assert.True(citizenRoles.SetEquals(expected),
            "Citizen roles must be exactly the human rig's roles minus the pinky roles. "
            + $"Missing: [{string.Join(", ", expected.Except(citizenRoles))}] "
            + $"Extra: [{string.Join(", ", citizenRoles.Except(expected))}]");

        // The pinky roles are genuinely unassigned (not mapped onto some other bone).
        var rig = LoadGeneratedTargetRig();
        foreach (var role in pinkyRoles)
            Assert.Null(rig.BoneForRole(role));
    }

    // ---------------------------------------------------------------- helpers

    private static HashSet<BoneRole> PresentRoles(TargetRig rig)
    {
        var roles = new HashSet<BoneRole>();
        for (var i = 0; i < rig.Skeleton.Count; i++)
        {
            if (rig.RoleOf(i) is { } role)
                Assert.True(roles.Add(role), $"Role {role} assigned twice.");
        }
        return roles;
    }

    private static int IndexOf(TargetRig rig, string boneName)
    {
        for (var i = 0; i < rig.Skeleton.Count; i++)
        {
            if (rig.Skeleton[i].Name == boneName)
                return i;
        }
        throw new InvalidOperationException($"Bone '{boneName}' not found in the citizen rig.");
    }

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
