#nullable enable annotations

using System;
using System.Collections.Generic;
using HumanoidRetargeter.Core.Cleanup;
using HumanoidRetargeter.Core.Formats;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Solve;
using HumanoidRetargeter.Core.Target;

namespace HumanoidRetargeter.Core;

/// <summary>Options for <see cref="Retargeter.ConvertBatch"/> output assembly.</summary>
public sealed class BatchOptions
{
    /// <summary>
    /// When set, the batch additionally augments this existing vmdl text (all successful
    /// clips spliced into its AnimationList via <see cref="VmdlAugmenter"/>) and returns the
    /// result in <see cref="RetargetBatchResult.AugmentedVmdl"/>.
    /// </summary>
    public string? AugmentVmdlText { get; init; }

    /// <summary>Assets-relative folder the DMX files will be written to by the caller; used
    /// to build each AnimFile's <c>source_filename</c>.</summary>
    public string DmxFolderRelative { get; init; } = "animations/retargeted";

    /// <summary>
    /// Animation source paths (<c>source_filename</c> values of the augment target's existing
    /// AnimFile nodes, assets-relative) that the IO-owning caller has determined NO LONGER
    /// EXIST on disk. Stale AnimFile entries referencing them are REMOVED from the augmented
    /// vmdl (reported on <see cref="RetargetBatchResult.Warnings"/>) — one unresolvable
    /// source otherwise fails the ENTIRE vmdl recompile ("Node 'X' resolve failure"), taking
    /// every newly added animation down with it. Entries this batch overwrites (their DMX is
    /// about to be written) are never pruned. The facade itself never touches the filesystem:
    /// callers probe <see cref="Target.VmdlAugmenter.CollectAnimSourcePaths"/> results against
    /// their content roots and pass the missing ones here. Null/empty = keep everything.
    /// </summary>
    public IReadOnlyCollection<string>? MissingAnimSources { get; init; }

    /// <summary>Auto-suffix colliding clip names (<c>_2</c>, <c>_3</c>, …) across the whole
    /// batch (default on). When off, duplicate names are kept as-is.</summary>
    public bool AutoSuffixCollisions { get; init; } = true;

    /// <summary>
    /// After conversion, scan the batch's successful clip names for directional locomotion
    /// families (default OFF): <c>_N</c>/<c>_NE</c>/…/<c>_NW</c> compass suffixes and
    /// <c>_Forward</c>/<c>_Backward</c>(/<c>_Back</c>)/<c>_Left</c>/<c>_Right</c> word forms
    /// sharing a stem. Each complete family (all four cardinals) is grouped under a Folder
    /// node with a <c>2DBlend</c> wired to the citizen <c>move_x</c>/<c>move_y</c> pose
    /// parameters, replicating the shipped citizen locomotion layout (see
    /// <see cref="Target.LocomotionSetDetector"/>); detection results land on
    /// <see cref="RetargetBatchResult.LocomotionSets"/>. Custom (non-citizen) base models
    /// must declare <c>move_x</c>/<c>move_y</c> pose parameters themselves for the blends to
    /// be drivable.
    /// </summary>
    public bool DetectLocomotionSets { get; init; }
}
