#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Core.Mapping;

/// <summary>
/// Shared confidence model for preset detection and the auto-mapper. A humanoid mapping is
/// usable when 15 required slots are filled — Hips, at least one spine bone, Neck-or-Head,
/// and the upper-arm/lower-arm/hand/upper-leg/lower-leg/foot chains on both sides.
/// Everything else (more spine bones, clavicles, toes, fingers) is optional and contributes
/// the remaining weight.
/// </summary>
/// <remarks>
/// Every missing REQUIRED slot additionally penalizes the score multiplicatively
/// (<see cref="MissingRequiredPenalty"/>). This keeps detection honest: rich optional
/// coverage (e.g. 30 matching finger names) can never carry a preset over
/// <see cref="ProfileDetector.DetectionThreshold"/> when a required limb is unmatched —
/// with one slot missing the ceiling is (0.75·14/15 + 0.25)·0.75 ≈ 0.71 &lt; 0.8. A preset
/// only claims a rig when all 15 required slots resolve; otherwise the auto-mapper (which
/// sees the same names with more flexibility) takes over. Found via
/// Neutral_throw_ball_001__A057.bvh (NVIDIA SOMA skeleton): its mixamo-identical upper
/// body + finger names scored 0.90 while both upper legs were unmapped (SOMA's "LeftLeg"
/// is the thigh, mixamo's is the calf).
/// </remarks>
internal static class MappingConfidence
{
    private const float RequiredWeight = 0.75f;
    private const float OptionalWeight = 0.25f;

    /// <summary>Multiplicative score penalty applied once per missing required slot.</summary>
    private const float MissingRequiredPenalty = 0.75f;

    private static readonly BoneRole[] SpineRoles =
        { BoneRole.Spine0, BoneRole.Spine1, BoneRole.Spine2, BoneRole.Spine3, BoneRole.Spine4 };

    /// <summary>Roles consumed by required slots — excluded from the optional pool.</summary>
    private static readonly HashSet<BoneRole> RequiredPool = new()
    {
        BoneRole.Hips, BoneRole.Spine0, BoneRole.Neck, BoneRole.Head,
        BoneRole.UpperArmL, BoneRole.UpperArmR, BoneRole.LowerArmL, BoneRole.LowerArmR,
        BoneRole.HandL, BoneRole.HandR,
        BoneRole.UpperLegL, BoneRole.UpperLegR, BoneRole.LowerLegL, BoneRole.LowerLegR,
        BoneRole.FootL, BoneRole.FootR,
    };

    /// <summary>
    /// Confidence in [0, 1] of a set of mapped roles. <paramref name="definedRoles"/> is
    /// the universe of roles the mapper could have produced (a profile's alias keys, or all
    /// roles for the auto-mapper); optional coverage is measured against it.
    /// </summary>
    public static float Compute(IEnumerable<BoneRole> mappedRoles, IEnumerable<BoneRole> definedRoles)
    {
        var mapped = mappedRoles as ICollection<BoneRole> ?? mappedRoles.ToList();
        var mappedSet = new HashSet<BoneRole>(mapped);

        var requiredSatisfied = 0;
        const int requiredTotal = 15;
        if (mappedSet.Contains(BoneRole.Hips)) requiredSatisfied++;
        if (SpineRoles.Any(mappedSet.Contains)) requiredSatisfied++;
        if (mappedSet.Contains(BoneRole.Neck) || mappedSet.Contains(BoneRole.Head)) requiredSatisfied++;
        foreach (var role in RequiredPool)
        {
            if (role is BoneRole.Hips or BoneRole.Spine0 or BoneRole.Neck or BoneRole.Head)
                continue;
            if (mappedSet.Contains(role))
                requiredSatisfied++;
        }

        var requiredFraction = requiredSatisfied / (float)requiredTotal;
        var missingPenalty = MathF.Pow(MissingRequiredPenalty, requiredTotal - requiredSatisfied);

        var optionalDefined = definedRoles.Where(r => !RequiredPool.Contains(r)).Distinct().ToList();
        if (optionalDefined.Count == 0)
            return requiredFraction * missingPenalty;

        var optionalFraction = optionalDefined.Count(mappedSet.Contains) / (float)optionalDefined.Count;
        return (RequiredWeight * requiredFraction + OptionalWeight * optionalFraction) * missingPenalty;
    }
}
