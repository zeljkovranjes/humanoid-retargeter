using System;
using System.Collections.Generic;
using System.Numerics;
using HumanoidRetargeter.Mapping;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Target;

namespace HumanoidRetargeter.Solve;

using Vector3 = System.Numerics.Vector3; // s&box compat: shadow engine's global-namespace Vector3 (see Code/HumanoidRetargeter/Assembly.cs)

/// <summary>
/// Mirrors a solved TARGET-space clip across the target character's sagittal plane,
/// producing the left/right-swapped twin of an animation (e.g. a right-foot-lead walk from a
/// left-foot-lead one).
/// </summary>
/// <remarks>
/// <para><b>Mirror plane.</b> The plane through the rig-space origin spanned by the target
/// character's up and forward directions; its normal is the character's LATERAL axis,
/// computed from the target rig's rest geometry via <see cref="CharacterFrame"/> (never
/// hardcoded — an arbitrary target may be authored in any axis convention). When the
/// computed lateral lies on a coordinate axis up to float dirt (&lt; 1e-3 on the other two
/// components — true for every axis-aligned authored rig, including the s&amp;box citizen
/// rigs), it is snapped to that exact axis, which makes every reflection below an EXACT
/// sign-flip in IEEE arithmetic and therefore the whole mirror a bit-exact involution
/// (mirror ∘ mirror == identity, verified by test).</para>
/// <para><b>Math.</b> Let M = I − 2n̂n̂ᵀ be the reflection across the plane with unit normal
/// n̂. A world transform W = (R, t) maps to its mirror image by conjugation:
/// W′ = M̂ ∘ W ∘ M̂ (M̂ is its own inverse), giving rotation R′ = M·R·M and translation
/// t′ = M·t. For a quaternion q = (v, w), M·R·M is the rotation by the SAME angle about the
/// REFLECTED axis with REVERSED sense (a reflection flips orientation), i.e.
/// q′ = (2(n̂·v)n̂ − v, w); with n̂ = +X that is exactly q′ = (x, −y, −z, w), and positions
/// reflect as p′ = p − 2(n̂·p)n̂ = (−pₓ, p_y, p_z).</para>
/// <para><b>Locals, not worlds.</b> Because conjugation is a homomorphism
/// (M̂(AB)M̂ = (M̂AM̂)(M̂BM̂)) and world transforms are products of locals down the
/// hierarchy, mirroring every LOCAL transform and permuting bones by their L↔R partner is
/// exactly equivalent to mirroring the FK worlds — provided the partner permutation is
/// hierarchy-consistent (the partner's parent is the parent's partner), which is validated
/// and holds on structurally symmetric humanoid rigs. This avoids FK→inverse-FK float drift
/// entirely, which is what makes the double-mirror identity bit-exact.</para>
/// <para><b>Pairing.</b> Left/right bones are paired by the rig's canonical role annotations
/// first (UpperArmL ↔ UpperArmR, …); role-less bones (twist helpers, IK bones) fall back to
/// <c>_L</c>/<c>_R</c> name-token pairing (<c>arm_upper_L_twist0</c> ↔
/// <c>arm_upper_R_twist0</c>, <c>foot_L_IK_target</c> ↔ <c>foot_R_IK_target</c>); anything
/// unpaired (center bones: pelvis, spine, neck, head) mirrors in place, which reflects its
/// rotation across the sagittal plane and negates its lateral translation. IK-baked bones
/// should be re-baked from the mirrored body afterwards (<see cref="IkBoneBaker"/>) — the
/// pipeline does exactly that.</para>
/// </remarks>
public static class ClipMirror
{
    /// <summary>Maximum off-axis component magnitude below which the computed lateral axis is
    /// snapped to the exact coordinate axis (authored rigs are axis-aligned; the tiny rest
    /// asymmetries of a real mesh stay far below this).</summary>
    private const float AxisSnapTolerance = 1e-3f;

    /// <summary>
    /// Returns the mirrored copy of <paramref name="frames"/> (one new list, inputs
    /// untouched): per frame, bone i takes the conjugated local transform of its L↔R partner
    /// σ(i). See the class remarks for the math and pairing rules.
    /// </summary>
    /// <param name="frames">Solved per-frame local transforms (target skeleton bone order).</param>
    /// <param name="rig">The target rig (skeleton + roles) the frames belong to.</param>
    /// <exception cref="ArgumentException">Thrown when the rig maps a sided role without its
    /// counterpart, the pairing is not hierarchy-consistent, or the character frame is not
    /// computable — mirroring would silently produce garbage in those cases.</exception>
    public static List<XForm[]> Mirror(List<XForm[]> frames, TargetRig rig)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(rig);

        var skeleton = rig.Skeleton;
        var lateral = LateralAxis(rig);
        var pair = BuildPairing(rig);

        var result = new List<XForm[]>(frames.Count);
        foreach (var locals in frames)
        {
            if (locals.Length != skeleton.Count)
                throw new ArgumentException(
                    $"Frame has {locals.Length} bones but the target skeleton has {skeleton.Count}.",
                    nameof(frames));

            var mirrored = new XForm[locals.Length];
            for (var i = 0; i < locals.Length; i++)
            {
                var source = locals[pair[i]];
                mirrored[i] = new XForm(
                    ReflectPoint(source.Pos, lateral),
                    ReflectRotation(source.Rot, lateral));
            }
            result.Add(mirrored);
        }
        return result;
    }

    // ================================================================ mirror plane

    /// <summary>The unit mirror normal: the target character's lateral axis from rest
    /// geometry, snapped to an exact coordinate axis when within tolerance (bit-exact
    /// reflections, see class remarks).</summary>
    private static Vector3 LateralAxis(TargetRig rig)
    {
        Vector3 lateral;
        try
        {
            lateral = CharacterFrame.Compute(
                rig.Skeleton, rig.ToMappingResult(), rig.Skeleton.RestWorld).Lateral;
        }
        catch (ArgumentException e)
        {
            throw new ArgumentException(
                $"Cannot mirror: target character frame not computable ({e.Message}).", e);
        }

        var a = Vector3.Abs(lateral);
        if (a.Y <= AxisSnapTolerance && a.Z <= AxisSnapTolerance)
            return Vector3.UnitX;
        if (a.X <= AxisSnapTolerance && a.Z <= AxisSnapTolerance)
            return Vector3.UnitY;
        if (a.X <= AxisSnapTolerance && a.Y <= AxisSnapTolerance)
            return Vector3.UnitZ;
        return lateral; // general (non-axis-aligned) rig: exact involution is lost, math is not
    }

    /// <summary>p′ = p − 2(n̂·p)n̂. With a snapped axis this is an exact sign flip of one
    /// component (IEEE subtraction of representable values is exact).</summary>
    private static Vector3 ReflectPoint(Vector3 p, Vector3 n)
        => p - 2f * Vector3.Dot(p, n) * n;

    /// <summary>q′ = (2(n̂·v)n̂ − v, w): the conjugated rotation M·R·M — same angle, axis
    /// reflected, sense reversed. With n̂ = +X this is (x, −y, −z, w). Components are
    /// preserved exactly (no renormalization), keeping the double mirror bit-exact.</summary>
    private static Quaternion ReflectRotation(Quaternion q, Vector3 n)
    {
        var v = new Vector3(q.X, q.Y, q.Z);
        var reflected = 2f * Vector3.Dot(v, n) * n - v;
        return new Quaternion(reflected.X, reflected.Y, reflected.Z, q.W);
    }

    // ================================================================ L↔R pairing

    /// <summary>
    /// σ: bone → mirror partner (identity for center/unpaired bones). Roles pair first;
    /// role-less bones pair by <c>_L</c>/<c>_R</c> name tokens. Validated to be an involution
    /// consistent with the hierarchy (σ(parent(i)) == parent(σ(i))).
    /// </summary>
    private static int[] BuildPairing(TargetRig rig)
    {
        var skeleton = rig.Skeleton;
        var pair = new int[skeleton.Count];
        for (var i = 0; i < pair.Length; i++)
            pair[i] = i;

        for (var i = 0; i < skeleton.Count; i++)
        {
            if (rig.RoleOf(i) is { } role)
            {
                if (MirrorRole(role) is not { } mirroredRole)
                    continue; // center role: mirrors in place
                pair[i] = rig.BoneForRole(mirroredRole)
                    ?? throw new ArgumentException(
                        $"Cannot mirror: target rig maps role {role} ('{skeleton[i].Name}') "
                        + $"but not its counterpart {mirroredRole}.");
            }
            else
            {
                var partnerName = SwapSideTokens(skeleton[i].Name);
                if (partnerName is null)
                    continue; // no side token: center bone
                var partner = skeleton.IndexOf(partnerName);
                if (partner >= 0)
                    pair[i] = partner;
                // No partner bone: leave in place (e.g. an asymmetric prop bone) — its
                // rotation still mirrors across the sagittal plane.
            }
        }

        for (var i = 0; i < pair.Length; i++)
        {
            if (pair[pair[i]] != i)
                throw new ArgumentException(
                    $"Cannot mirror: bone pairing is not symmetric ('{skeleton[i].Name}' → "
                    + $"'{skeleton[pair[i]].Name}' → '{skeleton[pair[pair[i]]].Name}').");

            var parent = skeleton[i].ParentIndex;
            var partnerParent = skeleton[pair[i]].ParentIndex;
            var consistent = parent < 0
                ? partnerParent < 0
                : partnerParent == pair[parent];
            if (!consistent)
                throw new ArgumentException(
                    $"Cannot mirror: left/right pairing is not hierarchy-consistent — "
                    + $"'{skeleton[i].Name}' and partner '{skeleton[pair[i]].Name}' hang under "
                    + "non-mirrored parents.");
        }

        return pair;
    }

    /// <summary>UpperArmL → UpperArmR (and back); null for center roles. Every sided
    /// <see cref="BoneRole"/> ends in <c>L</c>/<c>R</c>; no center role does.</summary>
    private static BoneRole? MirrorRole(BoneRole role)
    {
        var name = role.ToString();
        var mirroredName = name[^1] switch
        {
            'L' => name[..^1] + "R",
            'R' => name[..^1] + "L",
            _ => null,
        };
        return mirroredName is not null && Enum.TryParse<BoneRole>(mirroredName, out var mirrored)
            ? mirrored
            : null;
    }

    /// <summary>Swaps <c>L</c>/<c>R</c> underscore-delimited name tokens
    /// (<c>foot_L_IK_target</c> → <c>foot_R_IK_target</c>); null when the name carries no
    /// side token.</summary>
    private static string? SwapSideTokens(string name)
    {
        var tokens = name.Split('_');
        for (var i = 0; i < tokens.Length; i++)
        {
            tokens[i] = tokens[i] switch
            {
                "L" => "R",
                "R" => "L",
                "l" => "r",
                "r" => "l",
                _ => tokens[i],
            };
        }
        var result = string.Join('_', tokens);
        return string.Equals(result, name, StringComparison.Ordinal) ? null : result;
    }
}
