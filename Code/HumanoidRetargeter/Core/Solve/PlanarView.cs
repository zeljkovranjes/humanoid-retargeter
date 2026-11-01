#nullable enable annotations

namespace HumanoidRetargeter.Core.Solve;

/// <summary>
/// The orthographic camera <see cref="PlanarSolver"/> looks at the source through, relative
/// to the way the character faces as the clip starts rather than to file axes: yaw 0 faces
/// the character, 90 sees it from its right side so it faces screen-right, 180 sees its back.
/// Positive pitch looks from above. The camera holds still if the clip turns.
/// </summary>
public readonly record struct PlanarView(float YawDegrees, float PitchDegrees = 0f)
{
    /// <summary>The character in profile, facing screen-right.</summary>
    public static PlanarView Side => new(90f);

    /// <summary>The character facing the camera.</summary>
    public static PlanarView Front => new(0f);

    /// <summary>The character facing away from the camera.</summary>
    public static PlanarView Back => new(180f);

    /// <summary>Turned 45 degrees, seen slightly from above.</summary>
    public static PlanarView ThreeQuarter => new(45f, 10f);
}
