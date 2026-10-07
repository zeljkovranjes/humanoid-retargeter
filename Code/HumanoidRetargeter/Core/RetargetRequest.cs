#nullable enable annotations

using System;
using System.Collections.Generic;
using HumanoidRetargeter.Core.Cleanup;
using HumanoidRetargeter.Core.Formats;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Solve;
using HumanoidRetargeter.Core.Target;

namespace HumanoidRetargeter.Core;

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

    /// <summary>Raw bytes of the source file (.fbx, .bvh, .glb, .gltf, .vrm, .anm, .an5 or .cba).</summary>
    public required byte[] SourceData { get; init; }

    /// <summary>
    /// Source file name (used for the report and DMX provenance). The extension drives the
    /// format choice (<c>.fbx</c> / <c>.bvh</c> / <c>.glb</c> / <c>.gltf</c> / <c>.vrm</c> —
    /// a VRM is a glTF container whose authored humanoid bone map becomes the mapping — /
    /// <c>.anm</c> / <c>.an5</c> RenderWare animations and <c>.cba</c> EA ANT packages,
    /// which additionally need <see cref="SkeletonData"/>); when the extension is unknown the content is sniffed
    /// (FBX binary magic / "FBXHeaderExtension" / BVH "HIERARCHY" / GLB 'glTF' magic /
    /// glTF JSON / RenderWare 0x1B animation chunk / ANTSTM3b).
    /// </summary>
    public required string SourceFileName { get; init; }

    /// <summary>
    /// Raw bytes of a companion SKELETON file for formats whose animation files carry no
    /// skeleton of their own: RenderWare <c>.anm</c>/<c>.an5</c> sources require the
    /// character model's <c>.dff</c>; EA ANT <c>.cba</c> sources require an ordered joint-table
    /// JSON (either the root array or an extracted <c>{ "joints": [...] }</c> wrapper).
    /// Callers resolve the companion file; the facade does no file IO. Ignored by
    /// self-contained formats. A request without its required companion fails clearly.
    /// </summary>
    public byte[]? SkeletonData { get; init; }

    /// <summary>
    /// Optional external-buffer reader for plain <c>.gltf</c> input. IO-owning callers
    /// resolve the URI relative to the document; GLB and data-URI glTF leave this null.
    /// </summary>
    public Func<string, byte[]>? ExternalBufferResolver { get; init; }

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
    /// When <see cref="ClipDefinitions"/> is set this index addresses the DEFINITIONS
    /// instead (each definition is what a UI row represents then).
    /// </summary>
    public int? TakeIndex { get; init; }

    /// <summary>
    /// Optional external clip definitions, parsed from a Unity <c>&lt;file&gt;.fbx.meta</c>
    /// sidecar (<see cref="UnityMeta.ParseClipAnimations"/>): Unity animation packs ship FBX
    /// files whose clips are sub-ranges of ONE source timeline. When set (non-empty), the
    /// conversion produces one output clip per definition instead of one per take: the
    /// definition's take (matched by <see cref="ExternalClipDef.TakeName"/>, falling back to
    /// the file's first take) is sliced to the definition's native-frame range
    /// (<see cref="UnityMeta.Slice"/>), named <see cref="ExternalClipDef.Name"/> (sanitized
    /// like take names, collision-suffixed across the batch) and looped per
    /// <see cref="ExternalClipDef.Loop"/> unless <see cref="LoopingOverride"/> is set.
    /// <see cref="TakeIndex"/> then indexes INTO this list. Null = no definitions.
    /// </summary>
    public IReadOnlyList<ExternalClipDef>? ClipDefinitions { get; init; }

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
    /// Copy the source clip's per-frame LOCAL translations onto same-named target bones
    /// (hips and its ancestors excluded — trajectory stays solver-owned). For SAME-RIG
    /// conversions of authored takes (a target FBX's own embedded animations): the solver
    /// pins every non-hips bone to its rest translation, silently dropping a Biped take's
    /// animated spine/thigh translations (~19cm of authored body sway on a death fall).
    /// Meaningless across different rigs — leave off (default) for real retargets.
    /// </summary>
    public bool PreserveSourceTranslations { get; init; }

    /// <summary>
    /// Optional arm end-effector IK pass pulling the wrists onto limb-length-normalized
    /// source hand positions. Default OFF: the geometric solver already matches anatomical
    /// directions, so arm IK only helps reach-critical work (props, contact poses) and can
    /// otherwise disturb elbow styling.
    /// </summary>
    public bool ArmEffectorIk { get; init; }

    /// <summary>
    /// Generate <c>AE_FOOTSTEP</c> AnimEvent nodes on each produced clip's vmdl AnimFile
    /// entry (default OFF). After solving and cleanup, settled contacts following foot
    /// lifts are detected on the SOLVED target clip, including in-place locomotion. Each
    /// touchdown becomes one footstep event, in the exact node
    /// shape the shipped citizen data uses (see <see cref="Target.FootstepEvents"/>).
    /// Skipped (with a report note) when the target rig lacks complete leg chains.
    /// </summary>
    public bool GenerateFootstepEvents { get; init; }

    /// <summary>
    /// Additionally produce a mirrored twin of every converted clip (default OFF), named
    /// <c>&lt;clip&gt;_M</c> (collision-suffixed across the batch as usual). Mirroring runs
    /// in TARGET space on the solved clip (<see cref="Solve.ClipMirror"/>): left/right role
    /// bone channels swap and everything is reflected across the target character's sagittal
    /// plane; IK-baked helper bones are re-baked from the mirrored body afterwards.
    /// </summary>
    public bool CreateMirroredVariant { get; init; }

    /// <summary>
    /// Additionally register an additive (delta) twin of every converted clip in the
    /// generated/augmented vmdl (default OFF), named <c>&lt;clip&gt;_delta</c> (the shipped
    /// citizen naming; collision-suffixed across the batch as usual). The twin is a second
    /// AnimFile entry REUSING the clip's DMX with an <c>AnimSubtract</c> child
    /// (<c>anim_name</c> = the base sequence, <c>frame</c> = <see cref="AdditiveReferenceFrame"/>) — the shipped
    /// <c>IdleLayer_01</c>/<c>IdleLayer_01_delta</c> pattern, where resourcecompiler
    /// subtracts the reference frame at compile time (no frame math happens here). The
    /// resulting <c>_delta</c> sequence is what s&amp;box layered animation additively
    /// blends on top of a base pose.
    /// </summary>
    public bool CreateAdditiveVariant { get; init; }

    /// <summary>Reference pose in the sampled output clip (zero-based). Defaults to its
    /// first frame; choose a neutral pose for the intended additive layer.</summary>
    public int AdditiveReferenceFrame { get; init; }

    /// <summary>Output clip name override; with multiple takes an index suffix is appended.
    /// Null = the source take name.</summary>
    public string? ClipNameOverride { get; init; }

    /// <summary>Force the looping flag on the output sequence(s); null = the source clip's flag.</summary>
    public bool? LoopingOverride { get; init; }
}
