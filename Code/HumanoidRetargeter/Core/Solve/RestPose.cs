#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Numerics;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Maths;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Core.Solve;
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
