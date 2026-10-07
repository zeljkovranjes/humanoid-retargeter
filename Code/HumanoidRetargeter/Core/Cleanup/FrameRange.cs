#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Numerics;
using HumanoidRetargeter.Core.Maths;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Core.Cleanup;
using Vector3 = System.Numerics.Vector3; // s&box compat: shadow engine's global-namespace Vector3 (see Code/HumanoidRetargeter/Assembly.cs)

/// <summary>Inclusive frame range.</summary>
public readonly record struct FrameRange(int Start, int End)
{
    /// <summary>Number of frames in the range.</summary>
    public int Length => End - Start + 1;
}
