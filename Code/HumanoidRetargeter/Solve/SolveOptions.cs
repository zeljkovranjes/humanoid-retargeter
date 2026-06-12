namespace HumanoidRetargeter.Solve;

/// <summary>Options controlling a single retarget solve (one clip → one output clip).</summary>
public sealed class SolveOptions
{
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
