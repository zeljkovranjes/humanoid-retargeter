#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Target;
using HumanoidRetargeter.Core.Skeleton;

namespace HumanoidRetargeter.Core;

/// <summary>Result of a single-file <see cref="Retargeter.Convert"/> (all takes in the file).</summary>
public sealed class RetargetResult
{
    /// <summary>One result per take in the source file.</summary>
    public required IReadOnlyList<ClipResult> Clips { get; init; }

    /// <summary>Standalone vmdl text registering every successful clip.</summary>
    public required string StandaloneVmdl { get; init; }

    /// <summary>Aggregated error messages (one per failed clip).</summary>
    public required IReadOnlyList<string> Errors { get; init; }

    /// <summary>True when at least one clip was produced and none failed.</summary>
    public bool Success => Clips.Count > 0 && Clips.All(c => c.Success);
}
