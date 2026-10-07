using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Target;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Mapping;

/// <summary>
/// The "sbox" preset (user report 2026-07-04): a COMPILED s&amp;box model picked as a
/// custom conversion target was rejected with "not recognized as humanoid (5%)" because no
/// preset knew the citizen family's reversed-word bone names (arm_upper_L, leg_upper_L) and
/// generic token matching expects upper_arm/thigh order. The preset must claim both shipped
/// rigs at full-confidence detection, and its alias table must stay in lockstep with
/// <see cref="SboxBoneClassifier"/>'s curated role table.
/// </summary>
public class SboxProfileTests
{
    [Theory]
    [InlineData("rig_human_male.json")] // 5-finger human rig (94 bones)
    [InlineData("rig_citizen.json")]    // classic 4-finger citizen (95 bones)
    public void Detect_SboxRigs_PicksSboxPreset(string fixture)
    {
        var (skeleton, _) = RigJson.Load(
            File.ReadAllText(MappingFixtures.FixturePath(fixture)));

        var detected = ProfileDetector.Detect(skeleton);

        Assert.NotNull(detected);
        var (profile, result) = detected.Value;
        Assert.Equal("sbox", profile.Name);
        Assert.True(result.Confidence >= 0.9f, $"Confidence {result.Confidence} < 0.9");

        // Core anatomy resolves onto the expected bones.
        Assert.Equal("pelvis", skeleton[result.RoleToBone[BoneRole.Hips]].Name);
        Assert.Equal("head", skeleton[result.RoleToBone[BoneRole.Head]].Name);
        Assert.Equal("neck_0", skeleton[result.RoleToBone[BoneRole.Neck]].Name);
        Assert.Equal("arm_upper_L", skeleton[result.RoleToBone[BoneRole.UpperArmL]].Name);
        Assert.Equal("leg_upper_R", skeleton[result.RoleToBone[BoneRole.UpperLegR]].Name);
        Assert.Equal("ankle_L", skeleton[result.RoleToBone[BoneRole.FootL]].Name);
        Assert.Equal("ball_R", skeleton[result.RoleToBone[BoneRole.ToeR]].Name);
    }

    /// <summary>Every alias of the sbox preset must agree with the curated
    /// <see cref="SboxBoneClassifier"/> role table, and every classifier entry must be an
    /// alias — the preset and the target-rig generator can never drift apart.</summary>
    [Fact]
    public void SboxPreset_AliasTable_MatchesSboxBoneClassifier()
    {
        var profile = ProfileLibrary.Sbox;

        foreach (var (role, aliases) in profile.Aliases)
        {
            foreach (var alias in aliases)
            {
                // spine_3 is aliased for forward-compat but exists on no shipped rig and
                // has no classifier entry; every other alias must round-trip exactly.
                if (alias == "spine_3")
                    continue;
                Assert.True(SboxBoneClassifier.RoleFor(alias) == role,
                    $"alias '{alias}' maps to {role} in the preset but "
                    + $"{SboxBoneClassifier.RoleFor(alias)?.ToString() ?? "null"} in SboxBoneClassifier");
            }
        }
    }

    /// <summary>The auto-mapper's generic tokens must also understand the reversed word
    /// order on its own (custom skeletons that follow the s&amp;box naming style without
    /// matching the citizen skeleton exactly).</summary>
    [Theory]
    [InlineData("arm_upper_L", BoneRole.UpperArmL)]
    [InlineData("arm_lower_R", BoneRole.LowerArmR)]
    [InlineData("leg_upper_L", BoneRole.UpperLegL)]
    [InlineData("leg_lower_R", BoneRole.LowerLegR)]
    public void AutoMapper_UnderstandsReversedLimbWordOrder(string boneName, BoneRole expected)
    {
        // Minimal rig around the probed bone so the mapper has hips context.
        var definitions = new List<BoneDefinition>
        {
            new("pelvis", null, HumanoidRetargeter.Core.Maths.XForm.Identity),
            new(boneName, "pelvis", HumanoidRetargeter.Core.Maths.XForm.Identity),
        };
        var skeleton = SkeletonModel.Create(definitions);

        var map = AutoMapper.Map(skeleton);

        Assert.True(map.RoleToBone.TryGetValue(expected, out var index)
            && skeleton[index].Name == boneName,
            $"auto-map missed {expected} for '{boneName}': "
            + string.Join(", ", map.RoleToBone.Select(kv => $"{kv.Key}={skeleton[kv.Value].Name}")));
    }
}
