#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Core.Mapping;

/// <summary>
/// Scores preset <see cref="Profile"/>s against a source skeleton and picks the best match.
/// Matching is exact on namespace-stripped, normalized names (see
/// <see cref="Profile.NormalizeName"/>), so helper bones (twists, share bones, IK markers)
/// are excluded simply by having no alias.
/// </summary>
public static class ProfileDetector
{
    /// <summary>Minimum confidence for <see cref="Detect"/> to accept a preset.</summary>
    public const float DetectionThreshold = 0.8f;

    /// <summary>
    /// Applies a profile to a skeleton: for each role, the first alias (in preference
    /// order) that names a not-yet-used source bone wins. Returns the full mapping with
    /// confidence and report notes.
    /// </summary>
    public static MappingResult Apply(Profile profile, SkeletonModel skeleton)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(skeleton);

        // Normalized stripped name → bone index; first occurrence wins on collisions.
        var byNormalizedName = new Dictionary<string, int>(skeleton.Count, StringComparer.Ordinal);
        foreach (var bone in skeleton.Bones)
            byNormalizedName.TryAdd(Profile.NormalizeName(profile.StripNamespace(bone.Name)), bone.Index);

        var result = new MappingResult(profile.Name, MappingSource.Preset);
        var usedBones = new HashSet<int>();
        var unmappedRoles = new List<BoneRole>();

        foreach (var role in Enum.GetValues<BoneRole>())
        {
            if (!profile.Aliases.TryGetValue(role, out var aliases))
                continue;

            var mapped = false;
            foreach (var alias in aliases)
            {
                if (byNormalizedName.TryGetValue(Profile.NormalizeName(alias), out var boneIndex)
                    && usedBones.Add(boneIndex))
                {
                    result.RoleToBone[role] = boneIndex;
                    mapped = true;
                    break;
                }
            }
            if (!mapped)
                unmappedRoles.Add(role);
        }

        if (unmappedRoles.Count > 0)
            result.Notes.Add($"Unmapped profile roles: {string.Join(", ", unmappedRoles)}");

        var ignored = skeleton.Bones.Where(b => !usedBones.Contains(b.Index)).Select(b => b.Name).ToList();
        if (ignored.Count > 0)
            result.Notes.Add(
                $"Ignored {ignored.Count} source bones: " +
                string.Join(", ", ignored.Take(12)) + (ignored.Count > 12 ? ", …" : string.Empty));

        result.Confidence = MappingConfidence.Compute(result.RoleToBone.Keys, profile.Aliases.Keys);
        return result;
    }

    /// <summary>Detection score of a profile for a skeleton (== the applied mapping's
    /// confidence): fraction of required humanoid roles matched, plus weighted optional
    /// roles. See <see cref="MappingConfidence"/>.</summary>
    public static float Score(Profile profile, SkeletonModel skeleton)
        => Apply(profile, skeleton).Confidence;

    /// <summary>
    /// Evaluates every profile (default: <see cref="ProfileLibrary.All"/>) and returns the
    /// best one with its full mapping, or null when none reaches
    /// <see cref="DetectionThreshold"/>.
    /// </summary>
    public static (Profile Profile, MappingResult Result)? Detect(
        SkeletonModel skeleton, IReadOnlyList<Profile>? profiles = null)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        profiles ??= ProfileLibrary.All;

        (Profile Profile, MappingResult Result)? best = null;
        foreach (var profile in profiles)
        {
            var result = Apply(profile, skeleton);
            if (best is null || result.Confidence > best.Value.Result.Confidence)
                best = (profile, result);
        }

        return best is { } candidate && candidate.Result.Confidence >= DetectionThreshold
            ? candidate
            : null;
    }
}
