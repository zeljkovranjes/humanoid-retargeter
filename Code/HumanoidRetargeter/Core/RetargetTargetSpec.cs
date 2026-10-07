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
/// The conversion target shared by all requests of one <see cref="Retargeter.Convert"/> /
/// <see cref="Retargeter.ConvertBatch"/> call: the rig plus the vmdl generation parameters.
/// </summary>
public sealed class RetargetTargetSpec
{
    /// <summary>The s&amp;box-source → engine-units vmdl scale (cm rigs like the citizen).</summary>
    public const float SboxSourceScale = 0.3937f;

    /// <summary>The committed asset path of the s&amp;box human male model.</summary>
    public const string SboxHumanMalePath = "models/citizen_human/citizen_human_male.vmdl";

    /// <summary>The committed asset path of the classic (4-finger) s&amp;box citizen model.</summary>
    public const string SboxCitizenPath = "models/citizen/citizen.vmdl";

    /// <summary>Target rig (skeleton + bone classes + roles).</summary>
    public required TargetRig Rig { get; set; }

    /// <summary>ModelModifier_ScaleAndMirror scale written into standalone vmdls:
    /// <c>0.3937</c> for cm-authored s&amp;box-source rigs, <c>1.0</c> for engine-unit rigs
    /// (the modifier node is omitted at 1.0).</summary>
    public required float VmdlScale { get; init; }

    /// <summary>base_model_name of generated standalone vmdls (the model that owns the mesh).</summary>
    public string BaseModelPath { get; init; } = "";

    /// <summary>
    /// Assets-relative mesh source file (FBX, GLB, or glTF) embedded in generated
    /// standalone vmdls as a <c>RenderMeshList/RenderMeshFile</c> node. Source-file targets
    /// have no compiled base model to point <see cref="BaseModelPath"/> at — without a mesh
    /// source their standalone vmdl compiles into an EMPTY model (0 bones, 0 sequences) and
    /// playing it does nothing. Callers own copying the file into the project (this type
    /// does no IO); settable so the editor can fill it at convert time once the output
    /// folder is known. Empty (default) = no mesh node.
    /// </summary>
    public string MeshFilePath { get; set; } = "";

    /// <summary>Apply the standalone ModelDoc mesh-import root-axis correction even
    /// when its mesh comes from a prefab and has no explicit <see cref="MeshFilePath"/>.</summary>
    public bool CompensateDmxRootYaw { get; set; }

    /// <summary>
    /// Import scale of <see cref="MeshFilePath"/> (raw mesh-file units → the target
    /// skeleton's units). resourcecompiler reads mesh files' raw values ignoring their unit
    /// metadata, while the importer normalizes the target skeleton to centimeters — a
    /// meters-authored model therefore needs 100 here (the importer's recorded
    /// source-unit→cm factor) for the mesh to match the animation skeleton.
    /// </summary>
    public float MeshImportScale { get; set; } = 1.0f;

    /// <summary>Source mesh instances to import separately, preserving their skin buffers.
    /// Null or a single name keeps the default whole-file import.</summary>
    public IReadOnlyList<string>? MeshImportNames { get; set; }

    /// <summary>
    /// Material remaps written into generated standalone vmdls as a
    /// MaterialGroupList/DefaultMaterialGroup (bare mesh material reference → assets-relative
    /// vmat path, e.g. <c>"mi_dante_head.vmat" → "animations/retargeted/mi_dante_head.vmat"</c>).
    /// FBX materials carry bare names the compiler cannot resolve as resource paths
    /// ("Trying to load an illegal resource name X.vmat"); this remap table — the same
    /// mechanism the shipped citizen vmdl uses — points them at real files. Null/empty =
    /// no material group node (default).
    /// </summary>
    public IReadOnlyDictionary<string, string>? MaterialRemaps { get; set; }

    /// <summary>
    /// Additional AnimFile entries appended to generated/augmented vmdls verbatim —
    /// the target FBX's OWN embedded animations (an FBX with an animation on it must keep
    /// that animation when new ones are retargeted onto it; the AnimFile references the
    /// FBX directly, exactly like the shipped citizen animation list references its
    /// Citizen@*.fbx files, so the import is lossless). Augmentation skips entries the
    /// existing vmdl already carries (idempotent re-runs). Null/empty = none (default).
    /// </summary>
    public IReadOnlyList<Target.AnimEntry>? ExtraAnimFiles { get; set; }

    /// <summary>default_root_bone_name of the generated AnimationList (also the bone vmdl
    /// ExtractMotion nodes operate on).</summary>
    public string DefaultRootBone { get; set; } = "pelvis";

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

    /// <summary>
    /// The classic (4-finger) s&amp;box citizen target: rig parsed from the committed
    /// <c>Assets/humanoid_retargeter/target_rig_sbox_citizen.json</c> text (callers do the
    /// file IO), 0.3937 vmdl scale, citizen base model, pelvis root, Y-up cm. The rig has no
    /// pinky bones, so pinky roles stay unassigned — the engine's own constraints handle the
    /// pinky at runtime for models that have one. Pass the committed SAME weight bytes as
    /// <paramref name="dlWeights"/> to enable the deep-learning solver.
    /// </summary>
    public static RetargetTargetSpec SboxCitizen(string targetRigJson, byte[]? dlWeights = null) => new()
    {
        Rig = TargetRig.Load(targetRigJson),
        VmdlScale = SboxSourceScale,
        BaseModelPath = SboxCitizenPath,
        DefaultRootBone = "pelvis",
        UpAxis = TargetUpAxis.YUpCm,
        DlWeights = dlWeights,
    };
}
