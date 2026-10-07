#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Numerics;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Solve;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Core.Dl;
using Vector3 = System.Numerics.Vector3; // s&box compat: shadow engine's global-namespace Vector3 (see Code/HumanoidRetargeter/Assembly.cs)

/// <summary>A batched per-frame source graph ready for <see cref="SameModel.Encode"/>.</summary>
public sealed class SameSourceGraph
{
    /// <summary>Normalized node features, flat [FrameCount·JointCount × 32].</summary>
    public required float[] X { get; init; }

    /// <summary>Edge sources (bidirectional + self-loops, all frames).</summary>
    public required int[] EdgeSrc { get; init; }

    /// <summary>Edge destinations.</summary>
    public required int[] EdgeDst { get; init; }

    /// <summary>Frame id per node.</summary>
    public required int[] Batch { get; init; }

    /// <summary>Number of feature frames (matches the clip's frame count in production
    /// mode; native frames − 2 in golden-parity mode).</summary>
    public required int FrameCount { get; init; }

    /// <summary>Graph joints per frame (hips subtree + end joints).</summary>
    public required int JointCount { get; init; }

    /// <summary>Graph node names within one frame (bone names; synthesized leaf tips get
    /// a <c>_end</c> suffix). For diagnostics and parity tests.</summary>
    public required string[] JointNames { get; init; }
}
