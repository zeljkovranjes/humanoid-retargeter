using System;
using HumanoidRetargeter.Cleanup;
using HumanoidRetargeter.Mapping;
using HumanoidRetargeter.Solve;
using HumanoidRetargeter.Target;

namespace HumanoidRetargeter;

/// <summary>
/// One source animation file to retarget (engine-agnostic: bytes in, no file IO). Every
/// request runs its OWN profile detection, so a single batch may mix Mixamo + ActorCore +
/// BVH sources — unless <see cref="MappingOverride"/> supplies a mapping explicitly.
/// </summary>
public sealed class RetargetRequest
{
    /// <summary>Raw bytes of the source file (.fbx or .bvh).</summary>
    public required byte[] SourceData { get; init; }

    /// <summary>
    /// Source file name (used for the report and DMX provenance). The extension drives the
    /// format choice (<c>.fbx</c> / <c>.bvh</c>); when the extension is unknown the content
    /// is sniffed (FBX binary magic / "FBXHeaderExtension" / BVH "HIERARCHY").
    /// </summary>
    public required string SourceFileName { get; init; }

    /// <summary>
    /// UI-supplied mapping (manual mapping table or a user preset loaded Editor-side).
    /// Null = auto-detect per request: preset profiles via <see cref="ProfileDetector"/>,
    /// then the <see cref="AutoMapper"/> as best-effort fallback.
    /// </summary>
    public MappingResult? MappingOverride { get; init; }

    /// <summary>Solver tunables (hip scales, finger transfer). ClipIndex/ClipName are managed
    /// by the pipeline per take and ignored here. Null = defaults.</summary>
    public SolveOptions? Solve { get; init; }

    /// <summary>
    /// Root-motion handling. <see cref="RootMotionMode.Extract"/> on a target without a
    /// dedicated animated root bone (the s&amp;box rig: pelvis is parentless, root_IK is
    /// IkBaked) leaves the frames untouched and instead sets the ExtractMotion flag on the
    /// clip's vmdl AnimFile entry — Source 2's compile-time extraction replaces the missing
    /// bone-level extraction. <see cref="RootMotionMode.InPlace"/> always operates on the
    /// hips directly.
    /// </summary>
    public RootMotionMode RootMotion { get; init; } = RootMotionMode.Off;

    /// <summary>Run the Kovar foot-plant cleanup pass on the solved frames (default on).</summary>
    public bool FootPlantCleanup { get; init; } = true;

    /// <summary>
    /// Optional arm end-effector IK pass pulling the wrists onto limb-length-normalized
    /// source hand positions. Default OFF: the geometric solver already matches anatomical
    /// directions, so arm IK only helps reach-critical work (props, contact poses) and can
    /// otherwise disturb elbow styling.
    /// </summary>
    public bool ArmEffectorIk { get; init; }

    /// <summary>Output clip name override; with multiple takes an index suffix is appended.
    /// Null = the source take name.</summary>
    public string? ClipNameOverride { get; init; }

    /// <summary>Force the looping flag on the output sequence(s); null = the source clip's flag.</summary>
    public bool? LoopingOverride { get; init; }
}

/// <summary>
/// The conversion target shared by all requests of one <see cref="Retargeter.Convert"/> /
/// <see cref="Retargeter.ConvertBatch"/> call: the rig plus the vmdl generation parameters.
/// </summary>
public sealed class RetargetTargetSpec
{
    /// <summary>The s&amp;box-source → engine-units vmdl scale (cm rigs like the citizen).</summary>
    public const float SboxSourceScale = 0.3937f;

    /// <summary>The committed asset path of the s&amp;box human male model.</summary>
    public const string SboxHumanMalePath = "models/citizen_human/citizen_human_male.vmdl";

    /// <summary>Target rig (skeleton + bone classes + roles).</summary>
    public required TargetRig Rig { get; init; }

    /// <summary>ModelModifier_ScaleAndMirror scale written into standalone vmdls:
    /// <c>0.3937</c> for cm-authored s&amp;box-source rigs, <c>1.0</c> for engine-unit rigs
    /// (the modifier node is omitted at 1.0).</summary>
    public required float VmdlScale { get; init; }

    /// <summary>base_model_name of generated standalone vmdls (the model that owns the mesh).</summary>
    public string BaseModelPath { get; init; } = "";

    /// <summary>default_root_bone_name of the generated AnimationList (also the bone vmdl
    /// ExtractMotion nodes operate on).</summary>
    public string DefaultRootBone { get; init; } = "pelvis";

    /// <summary>
    /// The shipped s&amp;box default target: rig parsed from the committed
    /// <c>Assets/humanoid_retargeter/target_rig_sbox.json</c> text (callers do the file IO),
    /// 0.3937 vmdl scale, citizen human male base model, pelvis root.
    /// </summary>
    public static RetargetTargetSpec SboxDefault(string targetRigJson) => new()
    {
        Rig = TargetRig.SboxDefault(targetRigJson),
        VmdlScale = SboxSourceScale,
        BaseModelPath = SboxHumanMalePath,
        DefaultRootBone = "pelvis",
    };
}

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

    /// <summary>Auto-suffix colliding clip names (<c>_2</c>, <c>_3</c>, …) across the whole
    /// batch (default on). When off, duplicate names are kept as-is.</summary>
    public bool AutoSuffixCollisions { get; init; } = true;
}
