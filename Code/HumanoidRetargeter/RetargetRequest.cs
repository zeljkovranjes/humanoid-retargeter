using System;
using HumanoidRetargeter.Cleanup;
using HumanoidRetargeter.Mapping;
using HumanoidRetargeter.Solve;
using HumanoidRetargeter.Target;

namespace HumanoidRetargeter;

/// <summary>Which solver retargets a request's clips (design §10).</summary>
public enum SolverKind
{
    /// <summary>The deterministic <see cref="Solve.GeometricSolver"/> (default; better
    /// wherever a role mapping exists).</summary>
    Geometric,

    /// <summary>The experimental skeleton-agnostic deep-learning solver
    /// (<see cref="Dl.DlSolver"/>, SAME pretrained checkpoint) — the no-profile fallback.
    /// Requires <see cref="RetargetTargetSpec.DlWeights"/>; ignores per-role mapping
    /// (only hips/alignment heuristics consult it) and leaves fingers at rest.</summary>
    DeepLearning,
}

/// <summary>
/// One source animation file to retarget (engine-agnostic: bytes in, no file IO). Every
/// request runs its OWN profile detection, so a single batch may mix Mixamo + ActorCore +
/// BVH sources — unless <see cref="MappingOverride"/> supplies a mapping explicitly.
/// </summary>
public sealed class RetargetRequest
{
    /// <summary>Solver choice for this request's clips. <see cref="SolverKind.DeepLearning"/>
    /// requires the batch's <see cref="RetargetTargetSpec.DlWeights"/> to be set; the
    /// conversion fails per-clip with a clear error otherwise.</summary>
    public SolverKind Solver { get; init; } = SolverKind.Geometric;

    /// <summary>Raw bytes of the source file (.fbx, .bvh, .glb or .gltf).</summary>
    public required byte[] SourceData { get; init; }

    /// <summary>
    /// Source file name (used for the report and DMX provenance). The extension drives the
    /// format choice (<c>.fbx</c> / <c>.bvh</c> / <c>.glb</c> / <c>.gltf</c>); when the
    /// extension is unknown the content is sniffed (FBX binary magic /
    /// "FBXHeaderExtension" / BVH "HIERARCHY" / GLB 'glTF' magic / glTF JSON).
    /// </summary>
    public required string SourceFileName { get; init; }

    /// <summary>
    /// Caller-supplied identity of this request, echoed verbatim on every produced
    /// <see cref="ClipResult.SourceId"/> so callers can join results back to their own
    /// entries unambiguously (e.g. the editor window passes the FULL file path here, since
    /// two files in different folders may share the same <see cref="SourceFileName"/>).
    /// Null = <see cref="SourceFileName"/>.
    /// </summary>
    public string? SourceId { get; init; }

    /// <summary>
    /// Import sample rate the source clips are resampled to (BVH native frames / FBX curves
    /// are evaluated on this grid). Null = the importer default (30 fps).
    /// </summary>
    public float? SampleFps { get; init; }

    /// <summary>
    /// Restricts the conversion to ONE take of the source file (0-based index into the
    /// imported scene's clips). Null = convert all takes. Out of range fails the request's
    /// clip result with a clear error (the batch continues). UI listings that expand a
    /// multi-take file into one entry per take submit one request per selected take.
    /// </summary>
    public int? TakeIndex { get; init; }

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
/// Axis/unit convention of a <see cref="RetargetTargetSpec"/>'s rig data — drives the DMX
/// axis-system declaration, foot-plant threshold units, and the editor preview's
/// rig-space → engine-space conversion.
/// </summary>
public enum TargetUpAxis
{
    /// <summary>
    /// The s&amp;box source convention: rig authored in centimeters, Y-up (the shipped
    /// citizen rig, FBX targets). The vmdl's ScaleAndMirror 0.3937 + resourcecompiler's
    /// Y-up→Z-up conversion take it to engine space at compile time. Default.
    /// </summary>
    YUpCm,

    /// <summary>
    /// Engine space already: rig read from a compiled model's <c>Model.Bones</c>
    /// (inches, Z-up). The DMX declares a Z-up axis system so the compiler performs no
    /// further axis conversion.
    /// </summary>
    ZUpEngine,
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
    /// Axis/unit convention of <see cref="Rig"/>. <see cref="TargetUpAxis.YUpCm"/> (default)
    /// for cm Y-up source-space rigs (DMX declares Y-up, compiler converts);
    /// <see cref="TargetUpAxis.ZUpEngine"/> for rigs read from compiled engine models
    /// (DMX declares Z-up so no double conversion happens at compile, and cm-tuned cleanup
    /// thresholds are rescaled to inches).
    /// </summary>
    public TargetUpAxis UpAxis { get; init; } = TargetUpAxis.YUpCm;

    /// <summary>
    /// Raw bytes of the committed SAME weight blob
    /// (<c>Assets/humanoid_retargeter/dl/same_v1.weights</c>; callers do the file IO).
    /// Required only when a request selects <see cref="SolverKind.DeepLearning"/>; the
    /// solver instance is built once per batch from these bytes.
    /// </summary>
    public byte[]? DlWeights { get; init; }

    /// <summary>
    /// The shipped s&amp;box default target: rig parsed from the committed
    /// <c>Assets/humanoid_retargeter/target_rig_sbox.json</c> text (callers do the file IO),
    /// 0.3937 vmdl scale, citizen human male base model, pelvis root. Pass the committed
    /// SAME weight bytes as <paramref name="dlWeights"/> to enable the deep-learning solver.
    /// </summary>
    public static RetargetTargetSpec SboxDefault(string targetRigJson, byte[]? dlWeights = null) => new()
    {
        Rig = TargetRig.SboxDefault(targetRigJson),
        VmdlScale = SboxSourceScale,
        BaseModelPath = SboxHumanMalePath,
        DefaultRootBone = "pelvis",
        DlWeights = dlWeights,
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
