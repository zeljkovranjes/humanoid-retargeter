using System;
using System.Collections.Generic;

namespace HumanoidRetargeter.Skeleton;

/// <summary>
/// The result of ingesting a source animation file: a skeleton with its rest pose plus the
/// file's clips resampled on a fixed fps grid.
/// </summary>
/// <remarks>
/// Unit policy: all translations are converted to <b>centimeters</b> at import time
/// (<see cref="UnitScaleCm"/> records the applied source-unit→cm factor for diagnostics).
/// Axis policy: the file's native axes are <b>preserved</b> — no axis conversion is performed.
/// The original FBX <c>GlobalSettings</c> axes are recorded so later pipeline stages can
/// interpret directions (axis indices: 0 = X, 1 = Y, 2 = Z).
/// </remarks>
public sealed class SourceScene
{
    /// <summary>Source skeleton with rest pose, in centimeters, native axes.</summary>
    public Skeleton Skeleton { get; }

    /// <summary>Clips resampled at a fixed fps; locals indexed in skeleton bone order.</summary>
    public IReadOnlyList<Clip> Clips { get; }

    /// <summary>Source-unit → centimeter factor that was applied to all translations at import.</summary>
    public float UnitScaleCm { get; }

    /// <summary>Up axis index from GlobalSettings (0 = X, 1 = Y, 2 = Z; FBX default 1).</summary>
    public int UpAxis { get; }

    /// <summary>Sign of the up axis (+1 or -1).</summary>
    public int UpAxisSign { get; }

    /// <summary>Front axis index from GlobalSettings (FBX default 2 = Z).</summary>
    public int FrontAxis { get; }

    /// <summary>Sign of the front axis (+1 or -1).</summary>
    public int FrontAxisSign { get; }

    /// <summary>Coordinate (right) axis index from GlobalSettings (FBX default 0 = X).</summary>
    public int CoordAxis { get; }

    /// <summary>Sign of the coordinate axis (+1 or -1).</summary>
    public int CoordAxisSign { get; }

    /// <summary>OriginalUpAxis from GlobalSettings (-1 when the exporter did not record one).</summary>
    public int OriginalUpAxis { get; }

    /// <summary>Creates a source scene container.</summary>
    public SourceScene(
        Skeleton skeleton,
        IReadOnlyList<Clip> clips,
        float unitScaleCm,
        int upAxis = 1, int upAxisSign = 1,
        int frontAxis = 2, int frontAxisSign = 1,
        int coordAxis = 0, int coordAxisSign = 1,
        int originalUpAxis = -1)
    {
        Skeleton = skeleton ?? throw new ArgumentNullException(nameof(skeleton));
        Clips = clips ?? throw new ArgumentNullException(nameof(clips));
        UnitScaleCm = unitScaleCm;
        UpAxis = upAxis;
        UpAxisSign = upAxisSign;
        FrontAxis = frontAxis;
        FrontAxisSign = frontAxisSign;
        CoordAxis = coordAxis;
        CoordAxisSign = coordAxisSign;
        OriginalUpAxis = originalUpAxis;
    }
}
