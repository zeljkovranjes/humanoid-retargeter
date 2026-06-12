using System.Collections.Generic;
using HumanoidRetargeter.Mapping;

namespace HumanoidRetargeter.Solve;

/// <summary>How a mapped role's rotation is transferred by the <see cref="GeometricSolver"/>.</summary>
public enum RoleTransferMode
{
    /// <summary>
    /// Absolute canonical-orientation matching: the target's animated chain direction is
    /// driven to <b>equal</b> the source's (in character-frame coordinates). Right for limbs
    /// and the spine — the pose IS the direction — but it also imposes the source rig's rest
    /// proportions/posture on roles whose rest directions legitimately differ between rigs.
    /// </summary>
    AbsoluteDirection,

    /// <summary>
    /// Rest-relative delta: the source's canonical-space rotation <i>delta from its own
    /// normalized rest</i> is replayed onto the <b>target's</b> normalized rest
    /// (<c>W_t(f) = C_t·ΔC(f)·C_t⁻¹·R_tgtNormRest</c> with
    /// <c>ΔC(f) = C_s⁻¹·ΔR(f)·C_s</c>). The target keeps its own rest carriage (shoulder
    /// line height, neck-base angle) and moves with the source. Identical to
    /// <see cref="AbsoluteDirection"/> when source and target rigs coincide.
    /// </summary>
    DeltaFromRest,
}

/// <summary>Options controlling a single retarget solve (one clip → one output clip).</summary>
public sealed class SolveOptions
{
    /// <summary>
    /// Default per-role transfer modes: shoulder girdle and neck carriage are
    /// <see cref="RoleTransferMode.DeltaFromRest"/> (each rig's clavicle line / neck-base
    /// direction is rig anatomy, not pose — absolute matching was measured to drag the
    /// s&amp;box shoulders 6–28° toward the source's flatter/lower clavicle line and is the
    /// "low shoulders, hunched neck" artifact). Everything else (limbs, spine, head, feet,
    /// fingers) stays absolute: there the worldspace direction IS the pose. Head stays
    /// absolute so gaze direction follows the source.
    /// </summary>
    public static IReadOnlyDictionary<BoneRole, RoleTransferMode> DefaultTransferModes { get; } =
        new Dictionary<BoneRole, RoleTransferMode>
        {
            [BoneRole.ClavicleL] = RoleTransferMode.DeltaFromRest,
            [BoneRole.ClavicleR] = RoleTransferMode.DeltaFromRest,
            [BoneRole.Neck] = RoleTransferMode.DeltaFromRest,
        };

    /// <summary>Per-role transfer mode overrides; roles not present use
    /// <see cref="DefaultTransferModes"/> (and roles absent there are
    /// <see cref="RoleTransferMode.AbsoluteDirection"/>). Replaces the defaults entirely
    /// when set — pass an empty dictionary for all-absolute (legacy) behavior.</summary>
    public IReadOnlyDictionary<BoneRole, RoleTransferMode>? TransferModes { get; init; }

    /// <summary>
    /// Scale applied to the pelvis translation components perpendicular to the character up
    /// direction. Null (default) = automatic: target hip height / source hip height, both
    /// measured on the normalized rests.
    /// </summary>
    public float? HipScaleHorizontal { get; init; }

    /// <summary>
    /// Scale applied to the pelvis translation component along the character up direction.
    /// Null (default) = the same automatic hip-height ratio as <see cref="HipScaleHorizontal"/>.
    /// </summary>
    public float? HipScaleVertical { get; init; }

    /// <summary>Whether finger roles are transferred; when false, target finger bones keep
    /// their rest locals.</summary>
    public bool TransferFingers { get; init; } = true;

    /// <summary>Output clip name; null = the source clip's name.</summary>
    public string? ClipName { get; init; }

    /// <summary>Index of the source clip to retarget (<c>SourceScene.Clips</c>).</summary>
    public int ClipIndex { get; init; }
}
