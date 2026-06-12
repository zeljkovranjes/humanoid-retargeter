using System;
using System.Collections.Generic;
using System.Numerics;
using HumanoidRetargeter.Mapping;
using HumanoidRetargeter.Maths;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Solve;

/// <summary>
/// Canonical anatomical frames: one world-space rest basis per mapped <see cref="BoneRole"/>,
/// derived from rest <b>geometry</b> (joint head positions) of any rig plus its mapping.
/// Built with the same deterministic convention on source and target, so world-rotation deltas
/// conjugated through these frames transfer between rigs with different bone local axes
/// (the s&amp;box Citizen rig's local axes encode no anatomy — bone-Y points chest-forward).
/// </summary>
/// <remarks>
/// <para><b>Frame convention</b> — for each role the frame quaternion <c>F</c> rotates unit
/// axes onto: <c>X = P</c> (primary), <c>Z</c> = the secondary hint <c>S</c> orthonormalized
/// against <c>P</c>, <c>Y = cross(Z, X)</c> (right-handed; for fingers Y is the curl hinge).</para>
/// <para><b>Primary axis P</b> = normalize(chain-child head − bone head), where the chain
/// child is the next <i>mapped</i> role down the bone's anatomical chain
/// (Hips→Spine0..4→Neck→Head; Clavicle→UpperArm→LowerArm→Hand; UpperLeg→LowerLeg→Foot→Toe;
/// per-finger Meta→Prox→Mid→Dist). Tips use virtual extensions: Head extends along character
/// up; Hand points at the midpoint of its mapped finger proximals (else along the forearm);
/// Foot without a toe and Toe extend along character forward; finger distals extend along
/// their previous segment. Other bones with nothing mapped below inherit the previous chain
/// segment's direction.</para>
/// <para><b>Secondary axis S</b> by bone class: spine/neck/head/hips and legs use character
/// forward (knee hinge lateral); clavicle/arms/hands use <c>cross(P, characterUp)</c>
/// (elbow hinge ⊥ limb in the character's horizontal plane at T-pose), falling back to
/// character forward when P is vertical; feet/toes use character up; fingers use the hand's
/// dorsal palm normal (see <see cref="HandGeometry.Dorsal"/>) so a positive rotation about
/// frame Y curls fingertips toward the palm on both hands.</para>
/// <para>When used by the solver, build the frames on the <see cref="RestNormalizer"/>-
/// normalized rest via <see cref="Build(SkeletonModel, MappingResult, IReadOnlyList{XForm})"/>;
/// this class itself just measures whatever rest it is given.</para>
/// </remarks>
public sealed class CanonicalFrames
{
    private readonly Dictionary<BoneRole, Quaternion> _frames;

    /// <summary>Character forward (the direction the toes point at rest), unit length.</summary>
    public Vector3 CharacterForward { get; }

    /// <summary>Character up (hips toward shoulders at rest), unit length.</summary>
    public Vector3 CharacterUp { get; }

    /// <summary>Rest hip height above the lowest foot/toe point, along character up, cm.</summary>
    public float HipHeight { get; }

    private CanonicalFrames(
        Dictionary<BoneRole, Quaternion> frames, Vector3 forward, Vector3 up, float hipHeight)
    {
        _frames = frames;
        CharacterForward = forward;
        CharacterUp = up;
        HipHeight = hipHeight;
    }

    /// <summary>True when a canonical frame exists for <paramref name="role"/> (the role is
    /// mapped and its chain geometry is resolvable).</summary>
    public bool Has(BoneRole role) => _frames.ContainsKey(role);

    /// <summary>The world-space canonical rest frame of <paramref name="role"/>.</summary>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="Has"/> is false for
    /// the role.</exception>
    public Quaternion WorldFrameOf(BoneRole role)
        => _frames.TryGetValue(role, out var frame)
            ? frame
            : throw new InvalidOperationException($"No canonical frame for role {role} (not mapped or unresolvable).");

    /// <summary>Builds frames from the skeleton's bind rest (<c>skeleton.RestWorld</c>).</summary>
    public static CanonicalFrames Build(SkeletonModel skeleton, MappingResult map)
        => Build(skeleton, map, (skeleton ?? throw new ArgumentNullException(nameof(skeleton))).RestWorld);

    /// <summary>
    /// Builds frames from explicit rest world transforms (e.g. a <see cref="RestPose"/>
    /// produced by <see cref="RestNormalizer"/>), indexed like <c>skeleton.Bones</c>.
    /// </summary>
    public static CanonicalFrames Build(
        SkeletonModel skeleton, MappingResult map, IReadOnlyList<XForm> worldRest)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(worldRest);
        if (worldRest.Count != skeleton.Count)
            throw new ArgumentException(
                $"worldRest has {worldRest.Count} entries for a {skeleton.Count}-bone skeleton.");

        var cf = CharacterFrame.Compute(skeleton, map, worldRest);
        var frames = new Dictionary<BoneRole, Quaternion>();

        foreach (var (chain, kind, left) in Chains())
            BuildChainFrames(chain, kind, left, map, worldRest, cf, frames);

        return new CanonicalFrames(frames, cf.Forward, cf.Up, cf.HipHeight);
    }

    // ---------------------------------------------------------------- chain construction

    private enum ChainKind
    {
        Body,
        Arm,
        Leg,
        Finger,
    }

    private static IEnumerable<(BoneRole[] Chain, ChainKind Kind, bool Left)> Chains()
    {
        yield return (new[]
        {
            BoneRole.Hips, BoneRole.Spine0, BoneRole.Spine1, BoneRole.Spine2, BoneRole.Spine3,
            BoneRole.Spine4, BoneRole.Neck, BoneRole.Head,
        }, ChainKind.Body, false);

        foreach (var left in new[] { true, false })
        {
            var s = left ? "L" : "R";
            yield return (new[]
            {
                Role("Clavicle", s), Role("UpperArm", s), Role("LowerArm", s), Role("Hand", s),
            }, ChainKind.Arm, left);
            yield return (new[]
            {
                Role("UpperLeg", s), Role("LowerLeg", s), Role("Foot", s), Role("Toe", s),
            }, ChainKind.Leg, left);

            foreach (var finger in new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" })
            {
                yield return (new[]
                {
                    Role(finger + "Meta", s), Role(finger + "Prox", s),
                    Role(finger + "Mid", s), Role(finger + "Dist", s),
                }, ChainKind.Finger, left);
            }
        }
    }

    private static BoneRole Role(string baseName, string side) => Enum.Parse<BoneRole>(baseName + side);

    private static void BuildChainFrames(
        BoneRole[] chain, ChainKind kind, bool left, MappingResult map,
        IReadOnlyList<XForm> worldRest, CharacterFrame cf, Dictionary<BoneRole, Quaternion> frames)
    {
        // Collapse to the mapped chain members; gaps are skipped so e.g. a missing Spine1
        // makes Spine0 point straight at Spine2.
        var mapped = new List<(BoneRole Role, Vector3 Pos)>(chain.Length);
        foreach (var role in chain)
        {
            if (map.RoleToBone.TryGetValue(role, out var index))
                mapped.Add((role, worldRest[index].Pos));
        }

        Vector3? dorsal = kind == ChainKind.Finger ? HandGeometry.Dorsal(map, worldRest, left) : null;

        for (var i = 0; i < mapped.Count; i++)
        {
            var (role, pos) = mapped[i];
            Vector3? prevDir = i > 0 ? pos - mapped[i - 1].Pos : null;

            var primary = i + 1 < mapped.Count
                ? mapped[i + 1].Pos - pos
                : TipPrimary(kind, role, pos, prevDir, left, map, worldRest, cf);
            if (primary is null || primary.Value.LengthSquared() < 1e-8f)
                continue;

            var secondary = Secondary(kind, role, primary.Value, dorsal, cf);
            frames[role] = BasisFromPrimarySecondary(primary.Value, secondary, cf);
        }
    }

    /// <summary>Primary direction for the last mapped bone of a chain (virtual extensions).</summary>
    private static Vector3? TipPrimary(
        ChainKind kind, BoneRole role, Vector3 pos, Vector3? prevDir, bool left,
        MappingResult map, IReadOnlyList<XForm> worldRest, CharacterFrame cf)
    {
        switch (kind)
        {
            case ChainKind.Body:
                // Head extends along character up (virtual head-top point); a body chain that
                // ends early keeps its previous segment direction, defaulting to up.
                return role == BoneRole.Head ? cf.Up : prevDir ?? cf.Up;

            case ChainKind.Arm:
                if (role is BoneRole.HandL or BoneRole.HandR)
                {
                    var knuckles = HandGeometry.FingerProximalMidpoint(map, worldRest, left);
                    if (knuckles is not null)
                        return knuckles.Value - pos;
                }
                return prevDir; // along the forearm / previous segment; null → no frame

            case ChainKind.Leg:
                // Foot without a mapped toe, and the toe itself, extend along character
                // forward (toes point forward by the character-frame convention).
                if (role is BoneRole.FootL or BoneRole.FootR or BoneRole.ToeL or BoneRole.ToeR)
                    return cf.Forward;
                return prevDir;

            case ChainKind.Finger:
                if (prevDir is not null)
                    return prevDir; // distal tip extrapolates its previous segment
                // Single mapped finger bone: point away from the hand when possible.
                var handRole = left ? BoneRole.HandL : BoneRole.HandR;
                if (map.RoleToBone.TryGetValue(handRole, out var handIndex))
                    return pos - worldRest[handIndex].Pos;
                return null;

            default:
                return null;
        }
    }

    /// <summary>Secondary (Z) hint by bone class; see the class remarks for rationale.</summary>
    private static Vector3 Secondary(ChainKind kind, BoneRole role, Vector3 primary, Vector3? dorsal, CharacterFrame cf)
    {
        switch (kind)
        {
            case ChainKind.Body:
                return cf.Forward;

            case ChainKind.Arm:
            {
                var hinge = Vector3.Cross(Vector3.Normalize(primary), cf.Up);
                return hinge.LengthSquared() < 1e-6f ? cf.Forward : hinge;
            }

            case ChainKind.Leg:
                // Feet and toes lie near the character-forward direction, so they use up as
                // the secondary; thigh/calf use forward (knee hinge lateral).
                if (role is BoneRole.FootL or BoneRole.FootR or BoneRole.ToeL or BoneRole.ToeR)
                    return cf.Up;
                return cf.Forward;

            case ChainKind.Finger:
                if (dorsal is not null)
                    return dorsal.Value;
                var fallback = Vector3.Cross(Vector3.Normalize(primary), cf.Up);
                return fallback.LengthSquared() < 1e-6f ? cf.Forward : fallback;

            default:
                return cf.Forward;
        }
    }

    /// <summary>
    /// Orthonormal right-handed basis: <c>X = normalize(primary)</c>, <c>Z = secondary</c>
    /// Gram-Schmidt-orthonormalized against X (falling back to character forward, then up,
    /// then world axes when degenerate), <c>Y = cross(Z, X)</c>.
    /// </summary>
    private static Quaternion BasisFromPrimarySecondary(Vector3 primary, Vector3 secondary, CharacterFrame cf)
    {
        var x = Vector3.Normalize(primary);

        var z = Orthonormalized(secondary, x)
            ?? Orthonormalized(cf.Forward, x)
            ?? Orthonormalized(cf.Up, x)
            ?? Orthonormalized(Vector3.UnitZ, x)
            ?? Orthonormalized(Vector3.UnitX, x)!.Value;

        var y = Vector3.Cross(z, x);

        // System.Numerics matrices act on row vectors: the rows are the images of the unit
        // axes under the rotation (row1 = R*X, row2 = R*Y, row3 = R*Z).
        var m = new Matrix4x4(
            x.X, x.Y, x.Z, 0f,
            y.X, y.Y, y.Z, 0f,
            z.X, z.Y, z.Z, 0f,
            0f, 0f, 0f, 1f);

        return MathQ.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }

    private static Vector3? Orthonormalized(Vector3 hint, Vector3 x)
    {
        var z = hint - x * Vector3.Dot(hint, x);
        return z.LengthSquared() < 1e-6f ? null : Vector3.Normalize(z);
    }
}
