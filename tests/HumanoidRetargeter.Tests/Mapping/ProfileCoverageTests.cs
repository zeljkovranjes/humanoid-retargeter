using HumanoidRetargeter.Core.Mapping;
using Xunit;

namespace HumanoidRetargeter.Tests.Mapping;

/// <summary>
/// Animatable-role coverage gate for every shipped preset: a preset must carry aliases for
/// the full animatable role set its rig family provides — a preset that silently omits e.g.
/// finger aliases would leave those bones frozen at the target rest on every conversion
/// (exactly the user-reported SOMA finger bug, and why the OptiTrack/Cascadeur presets were
/// dropped instead of shipped fingerless). Genuinely absent bones are declared per preset
/// in the documented exception lists below; adding a new preset with unmapped fingers or
/// toes fails this gate unless the absence is declared (and justified) here.
/// </summary>
public class ProfileCoverageTests
{
    /// <summary>Core roles every humanoid preset must alias: hips, at least two spine
    /// links, neck, head, and both-sided clavicles, arms, hands, legs and feet.</summary>
    private static readonly BoneRole[] CoreRoles =
    {
        BoneRole.Hips, BoneRole.Spine0, BoneRole.Spine1, BoneRole.Neck, BoneRole.Head,
        BoneRole.ClavicleL, BoneRole.ClavicleR,
        BoneRole.UpperArmL, BoneRole.UpperArmR,
        BoneRole.LowerArmL, BoneRole.LowerArmR,
        BoneRole.HandL, BoneRole.HandR,
        BoneRole.UpperLegL, BoneRole.UpperLegR,
        BoneRole.LowerLegL, BoneRole.LowerLegR,
        BoneRole.FootL, BoneRole.FootR,
    };

    /// <summary>
    /// Rig families with NO finger joints — the only presets allowed to omit the 30
    /// Prox/Mid/Dist finger roles. Each entry documents why the bones are genuinely absent
    /// from the family, not merely unmapped:
    /// <list type="bullet">
    /// <item><c>rokoko_bvh</c> — Rokoko Smartsuit body BVH; hands are chain tips (Smartgloves
    /// ship separately).</item>
    /// <item><c>smpl</c> — the SMPL body model has no hand joints (that is SMPL-X, which has
    /// its own fully-fingered preset).</item>
    /// <item><c>classic_bvh</c> — MotionBuilder "Export to Character Studio" / ACCAD-style
    /// mocap BVH ends at the Wrist joints.</item>
    /// <item><c>xsens_mvn</c> — the 23-segment MVN body model ends at the Hand segments
    /// (Xsens gloves are separate data).</item>
    /// </list>
    /// </summary>
    private static readonly HashSet<string> FingerlessFamilies = new(StringComparer.Ordinal)
    {
        "rokoko_bvh", "smpl", "classic_bvh", "xsens_mvn",
    };

    /// <summary>
    /// Rig families with NO toe joints — the only presets allowed to omit ToeL/ToeR:
    /// <list type="bullet">
    /// <item><c>perception_neuron</c> — Axis Neuron BVH exports end at the Foot joints.</item>
    /// </list>
    /// </summary>
    private static readonly HashSet<string> ToelessFamilies = new(StringComparer.Ordinal)
    {
        "perception_neuron",
    };

    /// <summary>
    /// Rig families whose hands are PARTIALLY articulated — the finger roles listed here are
    /// genuinely absent from the rig, every other finger role must still be aliased. Unlike
    /// <see cref="FingerlessFamilies"/> (all-or-nothing) this covers rigs that model some
    /// digits and not others:
    /// <list type="bullet">
    /// <item><c>fight_night</c> — the EA boxer wears BOXING GLOVES. The only real finger
    /// joints are a two-segment thumb (<c>HandThumb1/2</c>, no distal) and one fused middle
    /// finger (<c>HandMiddle1/2</c> under the <c>InHandMiddle</c> metacarpal, no distal).
    /// Index, ring and pinky exist solely as glove SHELL bones (<c>*IndexGlove</c>,
    /// <c>*InHandRingGlove</c>, <c>*PinkyGlove</c>) — mesh helpers, not joints; aliasing
    /// them would pose the target's fingers from a glove surface.</item>
    /// </list>
    /// </summary>
    private static readonly Dictionary<string, HashSet<BoneRole>> PartialHandFamilies = new(StringComparer.Ordinal)
    {
        ["fight_night"] = new HashSet<BoneRole>(
            (from finger in new[] { "Index", "Ring", "Pinky" }
             from segment in new[] { "Prox", "Mid", "Dist" }
             from side in new[] { "L", "R" }
             select Enum.Parse<BoneRole>(finger + segment + side))
            .Concat(new[]
            {
                // No distal phalanx on either articulated digit.
                BoneRole.ThumbDistL, BoneRole.ThumbDistR,
                BoneRole.MiddleDistL, BoneRole.MiddleDistR,
            })),
    };

    public static IEnumerable<object[]> AllPresets()
        => ProfileLibrary.All.Select(p => new object[] { p.Name });

    [Theory]
    [MemberData(nameof(AllPresets))]
    public void Preset_AliasesTheFullAnimatableRoleSet(string presetName)
    {
        var profile = ProfileLibrary.All.Single(p => p.Name == presetName);

        foreach (var role in CoreRoles)
            AssertAliased(profile, role, "core humanoid role");

        if (ToelessFamilies.Contains(profile.Name))
        {
            Assert.False(
                profile.Aliases.ContainsKey(BoneRole.ToeL) || profile.Aliases.ContainsKey(BoneRole.ToeR),
                $"{profile.Name} is declared toeless but aliases toe roles - remove it from the exception list");
        }
        else
        {
            AssertAliased(profile, BoneRole.ToeL, "toe");
            AssertAliased(profile, BoneRole.ToeR, "toe");
        }

        var fingerRoles =
            from finger in new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" }
            from segment in new[] { "Prox", "Mid", "Dist" }
            from side in new[] { "L", "R" }
            select Enum.Parse<BoneRole>(finger + segment + side);
        if (FingerlessFamilies.Contains(profile.Name))
        {
            Assert.False(fingerRoles.Any(profile.Aliases.ContainsKey),
                $"{profile.Name} is declared fingerless but aliases finger roles - remove it from the exception list");
        }
        else if (PartialHandFamilies.TryGetValue(profile.Name, out var absent))
        {
            foreach (var role in fingerRoles)
            {
                if (absent.Contains(role))
                {
                    // Symmetry with the other branches: a stale exception entry (the rig
                    // gained the joint, or it was never missing) must fail the gate too.
                    Assert.False(profile.Aliases.ContainsKey(role),
                        $"{profile.Name} declares {role} genuinely absent but aliases it - "
                        + "remove it from the partial-hand exception list");
                }
                else
                {
                    AssertAliased(profile, role, "finger phalanx");
                }
            }
        }
        else
        {
            foreach (var role in fingerRoles)
                AssertAliased(profile, role, "finger phalanx");
        }
    }

    [Fact]
    public void CoverageExceptionLists_ContainOnlyShippedPresets()
    {
        var known = ProfileLibrary.All.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var name in FingerlessFamilies.Concat(ToelessFamilies).Concat(PartialHandFamilies.Keys))
            Assert.True(known.Contains(name), $"Exception list names unknown preset '{name}'");

        // A family is either fully fingerless or partially handed, never both.
        Assert.Empty(FingerlessFamilies.Intersect(PartialHandFamilies.Keys));
    }

    private static void AssertAliased(Profile profile, BoneRole role, string kind)
        => Assert.True(
            profile.Aliases.TryGetValue(role, out var aliases) && aliases.Length > 0,
            $"Preset '{profile.Name}' has no alias for {kind} {role}; if the rig family "
            + "genuinely lacks this bone, declare it in the documented exception lists in "
            + nameof(ProfileCoverageTests));
}
