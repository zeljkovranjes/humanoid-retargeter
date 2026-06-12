using System;
using System.Collections.Generic;
using System.Numerics;
using HumanoidRetargeter.Mapping;
using HumanoidRetargeter.Maths;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Solve;

using Vector3 = System.Numerics.Vector3; // s&box compat: shadow engine's global-namespace Vector3 (see Code/HumanoidRetargeter/Assembly.cs)

/// <summary>
/// A skeleton's rest pose as explicit world transforms (indexed like the skeleton's bones).
/// Produced by <see cref="RestNormalizer"/>; feed it to
/// <see cref="CanonicalFrames.Build(SkeletonModel, MappingResult, IReadOnlyList{XForm})"/>.
/// </summary>
public sealed class RestPose
{
    /// <summary>Rest world transforms per bone (positions in cm).</summary>
    public XForm[] WorldRest { get; init; } = Array.Empty<XForm>();
}

/// <summary>
/// Rest-pose detection (T-pose / A-pose) and normalization to a canonical T-pose.
/// Runs on <b>both</b> source and target rests before canonical frames are built, so deltas
/// measured against the normalized source rest apply cleanly to the normalized target rest
/// (the s&amp;box human rig itself rests in a strong A-pose, ~52° below horizontal).
/// </summary>
/// <remarks>
/// <para><b>Detection:</b> the angle of each (LowerArm.head − UpperArm.head) rest segment
/// against the character's horizontal lateral direction (per side, then averaged):
/// 0–15° → <see cref="DetectedPose.TPose"/>; 15–60° with the arm <i>below</i> horizontal →
/// <see cref="DetectedPose.APose"/>; anything else → <see cref="DetectedPose.Other"/>.
/// Legs are checked analogously against vertical for wide stances.</para>
/// <para><b>Normalization (swing-only, hierarchical, per limb chain — never the spine):</b>
/// each arm segment is swung about its joint so the chain matches the canonical T-pose:
/// upper arm → ±lateral (exactly horizontal), forearm → ±lateral (straight arm), hand →
/// ±lateral; every swing rotates all descendant world rests about the joint (positions orbit
/// the joint, orientations are premultiplied), so segment lengths never change. Hand roll is
/// then resolved to the palm-down convention by rotating about the limb axis until the hand's
/// geometric dorsal normal (<see cref="HandGeometry.Dorsal"/>) aligns with character up.
/// Legs are only normalized (thigh and calf swung to exactly −up) when a wide stance
/// (&gt; 15° off vertical) is detected — normal rigs keep their slight natural leg splay.</para>
/// </remarks>
public static class RestNormalizer
{
    /// <summary>Rest-pose family detected from the arm rest angle.</summary>
    public enum DetectedPose
    {
        /// <summary>Arms within 15° of horizontal.</summary>
        TPose,

        /// <summary>Arms 15–60° below horizontal.</summary>
        APose,

        /// <summary>Anything else (arms raised, missing, or extreme poses).</summary>
        Other,
    }

    /// <summary>What detection and normalization found and did; surfaced in the mapping report.</summary>
    public sealed class RestReport
    {
        /// <summary>Detected rest-pose family.</summary>
        public DetectedPose Detected { get; set; } = DetectedPose.Other;

        /// <summary>Average upper-arm rest angle against the horizontal lateral direction,
        /// degrees (0 = perfect T-pose).</summary>
        public float UpperArmAngleDeg { get; set; } = float.NaN;

        /// <summary>Human-readable notes: corrections applied, skipped steps, oddities.</summary>
        public List<string> Notes { get; } = new();
    }

    private const float TPoseMaxDeg = 15f;
    private const float APoseMaxDeg = 60f;
    private const float WideStanceMinDeg = 15f;

    /// <summary>
    /// Detects the rest pose of <paramref name="skeleton"/> and returns a T-pose-normalized
    /// copy of its rest world transforms plus a report. The skeleton itself is not modified.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the mapping lacks the bones the
    /// character frame needs (see <see cref="CharacterFrame.Compute"/>).</exception>
    public static (RestPose Normalized, RestReport Report) Normalize(SkeletonModel skeleton, MappingResult map)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        ArgumentNullException.ThrowIfNull(map);

        var world = new XForm[skeleton.Count];
        for (var i = 0; i < skeleton.Count; i++)
            world[i] = skeleton.RestWorld[i];

        // Arm/leg normalization never moves the hip or shoulder joints, so the character
        // frame computed on the input rest stays valid throughout.
        var cf = CharacterFrame.Compute(skeleton, map, world);
        var report = new RestReport();

        DetectArms(map, world, cf, report);
        NormalizeArms(skeleton, map, world, cf, report);
        NormalizeLegsIfWide(skeleton, map, world, cf, report);

        return (new RestPose { WorldRest = world }, report);
    }

    // ---------------------------------------------------------------- detection

    private static void DetectArms(MappingResult map, XForm[] world, CharacterFrame cf, RestReport report)
    {
        var angleSum = 0f;
        var count = 0;
        var allBelowOrLevel = true;

        foreach (var (upper, lower, sign) in new[]
        {
            (BoneRole.UpperArmL, BoneRole.LowerArmL, 1f),
            (BoneRole.UpperArmR, BoneRole.LowerArmR, -1f),
        })
        {
            if (!map.RoleToBone.TryGetValue(upper, out var u) || !map.RoleToBone.TryGetValue(lower, out var l))
                continue;
            var dir = world[l].Pos - world[u].Pos;
            angleSum += Deg(MathQ.AngleBetween(dir, cf.Lateral * sign));
            count++;
            // "Below horizontal" with a small tolerance so a T-pose arm 1° above still counts.
            allBelowOrLevel &= Vector3.Dot(Vector3.Normalize(dir), cf.Up) < 0.05f;
        }

        if (count == 0)
        {
            report.Detected = DetectedPose.Other;
            report.Notes.Add("Upper/lower arms unmapped; rest pose undetectable, no arm normalization.");
            return;
        }

        var angle = angleSum / count;
        report.UpperArmAngleDeg = angle;
        report.Detected = angle <= TPoseMaxDeg
            ? DetectedPose.TPose
            : angle <= APoseMaxDeg && allBelowOrLevel ? DetectedPose.APose : DetectedPose.Other;
        report.Notes.Add(
            $"Arm rest angle {angle:F1} deg from horizontal -> {report.Detected}.");
    }

    // ---------------------------------------------------------------- arms

    private static void NormalizeArms(
        SkeletonModel skeleton, MappingResult map, XForm[] world, CharacterFrame cf, RestReport report)
    {
        foreach (var (side, left, sign) in new[] { ("L", true, 1f), ("R", false, -1f) })
        {
            if (!TryBone(map, "UpperArm" + side, out var upper) || !TryBone(map, "LowerArm" + side, out var lower))
                continue;
            var lateral = cf.Lateral * sign;

            // 1. Swing the whole arm so (elbow - shoulder) hits exactly ±lateral.
            SwingSegment(skeleton, world, upper, world[lower].Pos - world[upper].Pos, lateral);

            // 2. Re-measure and swing the forearm so (hand - elbow) is also ±lateral (straight
            //    arm; elbow flexion is not introduced or removed, only swing).
            var hasHand = TryBone(map, "Hand" + side, out var hand);
            if (hasHand)
                SwingSegment(skeleton, world, lower, world[hand].Pos - world[lower].Pos, lateral);

            // 3. Swing the hand along the limb axis using its anatomical chain-child point
            //    (midpoint of the mapped finger proximals).
            if (hasHand)
            {
                var knuckles = HandGeometry.FingerProximalMidpoint(map, world, left);
                if (knuckles is not null)
                    SwingSegment(skeleton, world, hand, knuckles.Value - world[hand].Pos, lateral);

                // 4. Roll: rotate about the (now lateral) limb axis until the geometric dorsal
                //    normal points up -> the canonical palm-down T-pose convention.
                var dorsal = HandGeometry.Dorsal(map, world, left);
                if (dorsal is not null)
                {
                    var rollDeg = RollAboutAxis(skeleton, world, hand, lateral, dorsal.Value, cf.Up);
                    report.Notes.Add($"Hand {side}: palm-down roll correction {rollDeg:F1} deg.");
                }
                else
                {
                    report.Notes.Add($"Hand {side}: fingers unmapped/degenerate, palm roll left as-is.");
                }
            }
        }
    }

    // ---------------------------------------------------------------- legs

    private static void NormalizeLegsIfWide(
        SkeletonModel skeleton, MappingResult map, XForm[] world, CharacterFrame cf, RestReport report)
    {
        var down = -cf.Up;
        foreach (var side in new[] { "L", "R" })
        {
            if (!TryBone(map, "UpperLeg" + side, out var upper) || !TryBone(map, "LowerLeg" + side, out var lower))
                continue;

            var angle = Deg(MathQ.AngleBetween(world[lower].Pos - world[upper].Pos, down));
            if (angle <= WideStanceMinDeg)
                continue; // normal stance: leave the natural leg splay untouched

            report.Notes.Add($"Leg {side}: wide stance ({angle:F1} deg off vertical), normalized to vertical.");
            SwingSegment(skeleton, world, upper, world[lower].Pos - world[upper].Pos, down);
            if (TryBone(map, "Foot" + side, out var foot))
                SwingSegment(skeleton, world, lower, world[foot].Pos - world[lower].Pos, down);
        }
    }

    // ---------------------------------------------------------------- mechanics

    private static bool TryBone(MappingResult map, string roleName, out int bone)
        => map.RoleToBone.TryGetValue(Enum.Parse<BoneRole>(roleName), out bone);

    /// <summary>
    /// Swings the subtree rooted at <paramref name="joint"/> by the shortest-arc rotation
    /// taking <paramref name="currentDir"/> onto <paramref name="targetDir"/>, pivoting at the
    /// joint's head: descendant positions orbit the joint, orientations are premultiplied.
    /// </summary>
    private static void SwingSegment(
        SkeletonModel skeleton, XForm[] world, int joint, Vector3 currentDir, Vector3 targetDir)
        => RotateSubtree(skeleton, world, joint, MathQ.FromTo(currentDir, targetDir), world[joint].Pos);

    /// <summary>
    /// Rotates the subtree at <paramref name="joint"/> about <paramref name="axis"/> (through
    /// the joint) by the signed angle that brings <paramref name="currentRef"/>, projected ⊥
    /// axis, onto <paramref name="targetRef"/> projected ⊥ axis. Returns the applied angle in
    /// degrees.
    /// </summary>
    private static float RollAboutAxis(
        SkeletonModel skeleton, XForm[] world, int joint, Vector3 axis, Vector3 currentRef, Vector3 targetRef)
    {
        var a = currentRef - axis * Vector3.Dot(currentRef, axis);
        var b = targetRef - axis * Vector3.Dot(targetRef, axis);
        if (a.LengthSquared() < 1e-8f || b.LengthSquared() < 1e-8f)
            return 0f;

        var angle = MathF.Atan2(Vector3.Dot(Vector3.Cross(a, b), axis), Vector3.Dot(a, b));
        RotateSubtree(skeleton, world, joint, Quaternion.CreateFromAxisAngle(axis, angle), world[joint].Pos);
        return Deg(angle);
    }

    private static void RotateSubtree(
        SkeletonModel skeleton, XForm[] world, int root, Quaternion rotation, Vector3 pivot)
    {
        // Bones are topologically sorted, so a single forward pass finds the whole subtree.
        Span<bool> inSubtree = skeleton.Count <= 512 ? stackalloc bool[skeleton.Count] : new bool[skeleton.Count];
        inSubtree[root] = true;
        for (var i = root; i < skeleton.Count; i++)
        {
            var parent = skeleton[i].ParentIndex;
            if (i != root && (parent < 0 || !inSubtree[parent]))
                continue;
            if (i != root)
                inSubtree[i] = true;
            world[i] = new XForm(
                pivot + Vector3.Transform(world[i].Pos - pivot, rotation),
                MathQ.Normalize(rotation * world[i].Rot));
        }
    }

    private static float Deg(float radians) => radians * (180f / MathF.PI);
}
