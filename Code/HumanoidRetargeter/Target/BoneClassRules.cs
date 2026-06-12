using System;
using System.Text.RegularExpressions;

namespace HumanoidRetargeter.Target;

/// <summary>
/// Name-pattern <see cref="BoneClass"/> rules for arbitrary (user-picked) humanoid target
/// rigs, per design §1 "Custom targets". Used by <see cref="TargetRig.FromSkeleton"/> when
/// the target is not the shipped s&amp;box rig; the shipped target keeps its curated classes
/// (<see cref="SboxBoneClassifier"/> / the committed target-rig JSON) and never goes through
/// these heuristics.
///
/// Classification is best-effort by name (case-insensitive) and defaults to
/// <see cref="BoneClass.Animated"/>:
/// <list type="bullet">
/// <item><see cref="BoneClass.ConstraintDriven"/>: twist bones (s&amp;box
/// <c>arm_upper_L_twist0</c>, UE <c>upperarm_twist_01_l</c>, CC
/// <c>CC_Base_L_UpperarmTwist01</c>), helper/corrective bones (<c>helper</c>, <c>_hlp</c>,
/// <c>corrective</c>, CC <c>ShareBone</c>), and roll bones.</item>
/// <item><see cref="BoneClass.IkBaked"/>: IK markers (<c>_IK_</c>/<c>_IK</c> suffix, UE
/// <c>ik_</c> prefix, <c>IK_target</c>, s&amp;box <c>ikrule</c>/<c>aim_matrix_*</c>/
/// <c>hold_*</c>).</item>
/// </list>
/// Note that <see cref="TargetRig.FromSkeleton"/> forces any bone that carries a mapped role
/// to <see cref="BoneClass.Animated"/>, so a role assignment always wins over a twist-like
/// name (e.g. ActorCore's <c>CC_Base_NeckTwist01</c>, which IS the neck bone).
/// </summary>
public sealed class BoneClassRules
{
    // Plain cached Regex instead of [GeneratedRegex]: the s&box in-engine compiler
    // does not run the regex source generator (see SboxBoneClassifier).

    // Splits a bone name into word tokens: underscore-separated runs, camelCase humps,
    // and digit runs. A plain \btwist\b does not work because '_' is a word character
    // (no \b inside "_twist_") and CC names glue tokens together ("UpperarmTwist01").
    private static readonly Regex TokenRegex = new(@"[A-Z]?[a-z]+|[A-Z]+(?![a-z])|\d+");

    // Helper / corrective bones: s&box *_helper_*, generic _hlp/corrective, CC *ShareBone.
    private static readonly Regex HelperRegex = new(
        @"helper|_hlp|corrective|sharebone", RegexOptions.IgnoreCase);

    // Twist bones (s&box arm_upper_L_twist0, UE upperarm_twist_01_l,
    // CC CC_Base_L_UpperarmTwist01 / CC_Base_L_RibsTwist) and roll bones
    // (twist equivalents on some rigs: LeftForeArmRoll, ForeArmRoll_L, upperarm_roll_01_l),
    // matched as whole tokens so "wrist" or "troll" never false-positive.
    private static bool HasTwistOrRollToken(string boneName)
    {
        foreach (Match token in TokenRegex.Matches(boneName))
        {
            if (token.Value.Equals("twist", StringComparison.OrdinalIgnoreCase)
                || token.Value.Equals("roll", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // IK marker bones: s&box hand_L_IK_target/_IK_attach (_ik_), root_IK (_ik$),
    // hand_L_to_R_ikrule, aim_matrix_*, hold_L/R; UE ik_foot_root/ik_hand_gun (^ik_).
    private static readonly Regex IkRegex = new(
        @"_ik_|_ik$|^ik_|ik_target|ikrule|aim_matrix|^hold_", RegexOptions.IgnoreCase);

    /// <summary>Classifies a bone of an arbitrary humanoid rig by name.</summary>
    public BoneClass Classify(string boneName)
    {
        ArgumentNullException.ThrowIfNull(boneName);

        if (HasTwistOrRollToken(boneName) || HelperRegex.IsMatch(boneName))
            return BoneClass.ConstraintDriven;

        if (IkRegex.IsMatch(boneName))
            return BoneClass.IkBaked;

        return BoneClass.Animated;
    }
}
