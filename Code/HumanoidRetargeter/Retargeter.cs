using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using HumanoidRetargeter.Cleanup;
using HumanoidRetargeter.Formats.Bvh;
using HumanoidRetargeter.Formats.Dmx;
using HumanoidRetargeter.Formats.Fbx;
using HumanoidRetargeter.Mapping;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Solve;
using HumanoidRetargeter.Target;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter;

using Vector3 = System.Numerics.Vector3; // s&box compat: shadow engine's global-namespace Vector3 (see Code/HumanoidRetargeter/Assembly.cs)

/// <summary>
/// Engine-agnostic pipeline facade (design §5): bytes in → DMX + vmdl text out. Composes
/// import → mapping → solve → cleanup → IK baking → DMX → vmdl assembly. Batch is
/// first-class: N files × all takes per file → ONE combined vmdl, with per-request profile
/// detection (a batch may mix Mixamo + ActorCore + BVH sources) and per-clip failure
/// isolation. No file IO anywhere in this type — callers pass bytes/strings and write the
/// returned strings.
/// </summary>
/// <remarks>
/// Pipeline per clip:
/// <list type="number">
/// <item><b>Import</b> — format by file extension (<c>.fbx</c>/<c>.bvh</c>), content sniff
/// as fallback → <see cref="SourceScene"/> (cm, native axes).</item>
/// <item><b>Mapping</b> — per request: <see cref="RetargetRequest.MappingOverride"/> wins;
/// else <see cref="ProfileDetector.Detect"/> over the preset library (user presets are
/// loaded Editor-side and arrive as overrides); else <see cref="AutoMapper.Map"/> as best
/// effort with <see cref="MappingReportInfo.NeedsUserDecision"/> set when its confidence is
/// below <see cref="ProfileDetector.DetectionThreshold"/>.</item>
/// <item><b>Solve</b> — <see cref="GeometricSolver"/> per take (clip name = take name, or
/// <see cref="RetargetRequest.ClipNameOverride"/> + index).</item>
/// <item><b>Cleanup</b> (target space) — <see cref="FootPlant"/> when enabled (chains from
/// the target rig's UpperLeg/LowerLeg/Foot/Toe roles, up axis from the TARGET character
/// frame, fps from the clip); optional arm <see cref="EffectorIk"/> (off by default — the
/// solver already matches anatomical directions); <see cref="RootMotion"/> per mode (see
/// below).</item>
/// <item><b>IK baking</b> — <see cref="IkBoneBaker.Bake"/> when the rig has
/// <see cref="BoneClass.IkBaked"/> bones.</item>
/// <item><b>DMX</b> — <see cref="DmxWriter"/> over the target skeleton.</item>
/// <item><b>Assembly</b> — <see cref="VmdlWriter.GenerateStandalone"/> over all successful
/// clips, plus <see cref="VmdlAugmenter.Augment"/> when
/// <see cref="BatchOptions.AugmentVmdlText"/> is provided; clip-name collisions are
/// auto-suffixed (<c>_2</c>, <c>_3</c>, …) across the batch.</item>
/// </list>
/// <para><b>Root-motion ↔ target mapping.</b> <see cref="RootMotionMode.Extract"/> bakes the
/// ground-projected hips path onto the target's dedicated root bone — defined as a parentless
/// <see cref="BoneClass.Animated"/> bone that is an ancestor of (and distinct from) the Hips
/// bone. The s&amp;box rig has NO such bone (pelvis is itself parentless; root_IK is IkBaked),
/// so there Extract leaves the frames untouched, adds a report note, and instead sets
/// <see cref="AnimEntry.ExtractMotion"/> on the clip's vmdl entry: Source 2 extracts the
/// ground-plane translation from <see cref="RetargetTargetSpec.DefaultRootBone"/> at compile
/// time, which is the engine-native equivalent. The ExtractMotion flag is set for every
/// Extract request either way. <see cref="RootMotionMode.InPlace"/> always operates on the
/// hips channels directly and needs no dedicated root.</para>
/// </remarks>
public static class Retargeter
{
    /// <summary>Converts ALL takes of one source file. Equivalent to a one-request batch.</summary>
    public static RetargetResult Convert(RetargetRequest request, RetargetTargetSpec target)
    {
        ArgumentNullException.ThrowIfNull(request);
        var batch = ConvertBatch(new[] { request }, target);
        return new RetargetResult
        {
            Clips = batch.Clips,
            StandaloneVmdl = batch.StandaloneVmdl,
            Errors = batch.Errors,
        };
    }

    /// <summary>
    /// Converts a batch of source files against one target. Per-request profile detection,
    /// per-clip failure isolation (a bad file yields a failed <see cref="ClipResult"/> and
    /// the batch continues), one standalone vmdl with every successful clip, optional
    /// augmentation of an existing vmdl.
    /// </summary>
    public static RetargetBatchResult ConvertBatch(
        IReadOnlyList<RetargetRequest> requests, RetargetTargetSpec target, BatchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(target);
        options ??= new BatchOptions();

        var context = new TargetContext(target.Rig);
        var result = new RetargetBatchResult();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<AnimEntry>();

        foreach (var request in requests)
        {
            if (request is null)
                continue;
            ProcessRequest(request, target, context, options, usedNames, result.Clips, entries);
        }

        foreach (var clip in result.Clips)
        {
            if (!clip.Success)
                result.Errors.Add($"{clip.SourceFileName}: {clip.Error}");
        }

        result.StandaloneVmdl = VmdlWriter.GenerateStandalone(
            target.BaseModelPath, entries, target.VmdlScale, target.DefaultRootBone);

        if (options.AugmentVmdlText is not null)
        {
            try
            {
                result.AugmentedVmdl = VmdlAugmenter.Augment(options.AugmentVmdlText, entries, out _);
            }
            catch (Exception e)
            {
                result.Errors.Add($"vmdl augmentation failed: {e.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// Detection-only entry point for UI listings: imports the file and runs the same
    /// preset-then-auto mapping as conversion, without solving anything. Throws
    /// <see cref="FormatException"/> when the file is unreadable.
    /// </summary>
    public static MappingReportInfo Inspect(byte[] sourceData, string fileName)
    {
        ArgumentNullException.ThrowIfNull(sourceData);
        ArgumentNullException.ThrowIfNull(fileName);
        var scene = ImportSource(sourceData, fileName);
        var (map, needsUserDecision) = ResolveMapping(scene.Skeleton, mappingOverride: null);
        return BuildReport(map, needsUserDecision, scene.Skeleton);
    }

    // ================================================================ per-request pipeline

    private static void ProcessRequest(
        RetargetRequest request, RetargetTargetSpec target, TargetContext context,
        BatchOptions options, HashSet<string> usedNames,
        List<ClipResult> clips, List<AnimEntry> entries)
    {
        SourceScene scene;
        MappingReportInfo report;
        MappingResult map;
        try
        {
            scene = ImportSource(request.SourceData, request.SourceFileName);
            bool needsUserDecision;
            (map, needsUserDecision) = ResolveMapping(scene.Skeleton, request.MappingOverride);
            report = BuildReport(map, needsUserDecision, scene.Skeleton);
        }
        catch (Exception e)
        {
            clips.Add(new ClipResult
            {
                ClipName = FileStem(request.SourceFileName),
                SourceFileName = request.SourceFileName,
                Success = false,
                Error = e.Message,
            });
            return;
        }

        if (scene.Clips.Count == 0)
        {
            clips.Add(new ClipResult
            {
                ClipName = FileStem(request.SourceFileName),
                SourceFileName = request.SourceFileName,
                Mapping = report,
                Success = false,
                Error = "Source file contains no animation takes.",
            });
            return;
        }

        for (var take = 0; take < scene.Clips.Count; take++)
        {
            var clipName = UniqueClipName(
                RequestedClipName(request, scene, take), usedNames, options.AutoSuffixCollisions);
            try
            {
                var clip = SolveAndClean(request, target, context, scene, map, report, take, clipName);
                var dmxFileName = SanitizeFileName(clipName) + ".dmx";
                var dmx = DmxWriter.Write(target.Rig.Skeleton, clip, new DmxWriteOptions
                {
                    Name = clipName,
                    SourceNote = request.SourceFileName,
                });

                var extractMotion = request.RootMotion == RootMotionMode.Extract;
                clips.Add(new ClipResult
                {
                    ClipName = clipName,
                    SourceFileName = request.SourceFileName,
                    DmxFileName = dmxFileName,
                    DmxContent = dmx,
                    Mapping = report,
                    Success = true,
                    SolvedFrames = clip.Frames,
                    Fps = clip.Fps,
                    Looping = clip.Looping,
                    ExtractMotion = extractMotion,
                });
                entries.Add(new AnimEntry
                {
                    Name = clipName,
                    SourceFilename = JoinAssetPath(options.DmxFolderRelative, dmxFileName),
                    Looping = clip.Looping,
                    ExtractMotion = extractMotion,
                });
            }
            catch (Exception e)
            {
                clips.Add(new ClipResult
                {
                    ClipName = clipName,
                    SourceFileName = request.SourceFileName,
                    Mapping = report,
                    Success = false,
                    Error = e.Message,
                });
            }
        }
    }

    /// <summary>Solve one take and run the target-space cleanup + IK baking passes.</summary>
    private static Clip SolveAndClean(
        RetargetRequest request, RetargetTargetSpec target, TargetContext context,
        SourceScene scene, MappingResult map, MappingReportInfo report, int take, string clipName)
    {
        var requested = request.Solve ?? new SolveOptions();
        var solved = new GeometricSolver().Solve(scene, map, target.Rig, new SolveOptions
        {
            HipScaleHorizontal = requested.HipScaleHorizontal,
            HipScaleVertical = requested.HipScaleVertical,
            TransferFingers = requested.TransferFingers,
            ClipIndex = take,
            ClipName = clipName,
        });
        var frames = solved.Frames;

        // ---- foot-plant cleanup (target space; up axis from the TARGET character frame) ----
        if (request.FootPlantCleanup)
        {
            if (context.Up is { } up && context.FootChains is { } feet)
                FootPlant.Apply(frames, target.Rig.Skeleton, feet.Left, feet.Right, up, solved.Fps);
            else
                AddNote(report, "Foot-plant cleanup skipped: " + context.UpOrChainProblem);
        }

        // ---- optional arm effector IK (default off: the solver already matches anatomical
        // directions; arm IK is only for reach-critical work) ----
        if (request.ArmEffectorIk)
        {
            var problem = ArmIkCleanup.Apply(frames, scene, map, target.Rig, context, take);
            if (problem is not null)
                AddNote(report, "Arm effector IK skipped: " + problem);
        }

        // ---- root motion (see class remarks for the Extract ↔ ExtractMotion mapping) ----
        ApplyRootMotion(request.RootMotion, frames, context, report);

        // ---- IK helper bones (root_IK, IK targets, ikrule) need real baked channels ----
        if (context.HasIkBakedBones)
            IkBoneBaker.Bake(frames, target.Rig);

        var looping = request.LoopingOverride ?? solved.Looping;
        return new Clip(clipName, solved.Fps, looping, frames);
    }

    private static void ApplyRootMotion(
        RootMotionMode mode, List<XForm[]> frames, TargetContext context, MappingReportInfo report)
    {
        if (mode == RootMotionMode.Off)
            return;

        if (context.HipsIndex is not { } hips)
        {
            AddNote(report, $"Root motion ({mode}) skipped: target rig maps no Hips role.");
            return;
        }
        if (context.Up is not { } up)
        {
            AddNote(report, $"Root motion ({mode}) skipped: {context.UpOrChainProblem}");
            return;
        }

        if (mode == RootMotionMode.Extract && context.DedicatedRootIndex is null)
        {
            // s&box-style target: pelvis is parentless and there is no separate translating
            // root bone (root_IK is IkBaked). Bone-level extraction is impossible — the vmdl
            // AnimFile entry carries an ExtractMotion node instead (compile-time extraction).
            AddNote(report,
                "Root-motion extract: target has no dedicated animated root bone distinct from "
                + "the hips; frames left as solved and motion extraction delegated to the vmdl "
                + "AnimFile ExtractMotion node.");
            return;
        }

        var root = context.DedicatedRootIndex ?? hips;
        RootMotion.Apply(frames, new RootMotionAxes
        {
            Up = up,
            RootIndex = root,
            HipsIndex = hips,
            HipsParentIsRoot = context.HipsParentIsRoot,
        }, mode);
        AddNote(report, mode == RootMotionMode.Extract
            ? $"Root motion extracted onto dedicated root bone (index {root})."
            : "Root motion removed (in-place clip).");
    }

    private static void AddNote(MappingReportInfo report, string note)
    {
        if (!report.Notes.Contains(note))
            report.Notes.Add(note);
    }

    // ================================================================ import + mapping

    /// <summary>Extension picks the importer; unknown extensions fall back to content
    /// sniffing (FBX binary magic / ASCII header token / BVH "HIERARCHY").</summary>
    internal static SourceScene ImportSource(byte[] data, string fileName)
    {
        var ext = ExtensionOf(fileName);
        return ext switch
        {
            "fbx" => FbxImporter.Import(data),
            "bvh" => BvhImporter.Import(data),
            _ => SniffFormat(data) switch
            {
                "fbx" => FbxImporter.Import(data),
                "bvh" => BvhImporter.Import(data),
                _ => throw new FormatException(
                    $"Unrecognized source format for '{fileName}' (expected .fbx or .bvh)."),
            },
        };
    }

    private static string? SniffFormat(byte[] data)
    {
        // Binary FBX magic: "Kaydara FBX Binary  \0".
        const string fbxMagic = "Kaydara FBX Binary";
        if (StartsWithAscii(data, fbxMagic))
            return "fbx";

        var head = Encoding.UTF8.GetString(data, 0, Math.Min(data.Length, 4096));
        if (head.Contains("FBXHeaderExtension", StringComparison.Ordinal))
            return "fbx"; // ASCII FBX
        if (head.TrimStart().StartsWith("HIERARCHY", StringComparison.OrdinalIgnoreCase))
            return "bvh";
        return null;
    }

    private static bool StartsWithAscii(byte[] data, string prefix)
    {
        if (data.Length < prefix.Length)
            return false;
        for (var i = 0; i < prefix.Length; i++)
        {
            if (data[i] != (byte)prefix[i])
                return false;
        }
        return true;
    }

    /// <summary>
    /// Per-request mapping resolution: explicit override → preset detection → best-effort
    /// auto-map. <c>needsUserDecision</c> is true only on the auto path below the preset
    /// detection threshold — the conversion still proceeds with that map.
    /// </summary>
    private static (MappingResult Map, bool NeedsUserDecision) ResolveMapping(
        SkeletonModel skeleton, MappingResult? mappingOverride)
    {
        if (mappingOverride is not null)
            return (mappingOverride, false);

        if (ProfileDetector.Detect(skeleton) is { } detected)
            return (detected.Result, false);

        var auto = AutoMapper.Map(skeleton);
        return (auto, auto.Confidence < ProfileDetector.DetectionThreshold);
    }

    private static MappingReportInfo BuildReport(
        MappingResult map, bool needsUserDecision, SkeletonModel skeleton)
    {
        var report = new MappingReportInfo
        {
            ProfileName = map.ProfileName,
            Source = map.Source,
            Confidence = map.Confidence,
            NeedsUserDecision = needsUserDecision,
            MappedRoleCount = map.RoleToBone.Count,
            SkeletonSignature = Mapping.SkeletonSignature.Compute(skeleton),
        };
        report.Notes.AddRange(map.Notes);
        if (needsUserDecision)
            report.Notes.Add(
                $"No preset profile matched and auto-map confidence {map.Confidence:0.00} is below "
                + $"{ProfileDetector.DetectionThreshold:0.00}; proceeding with the best-effort auto map.");
        return report;
    }

    // ================================================================ naming

    private static string RequestedClipName(RetargetRequest request, SourceScene scene, int take)
    {
        var multipleTakes = scene.Clips.Count > 1;
        if (!string.IsNullOrWhiteSpace(request.ClipNameOverride))
            return multipleTakes ? $"{request.ClipNameOverride}_{take + 1}" : request.ClipNameOverride!;

        var takeName = scene.Clips[take].Name;
        if (!string.IsNullOrWhiteSpace(takeName) && takeName != "motion")
            return takeName;

        // BVH files (clip always named "motion") and unnamed takes use the file stem.
        var stem = FileStem(request.SourceFileName);
        return multipleTakes ? $"{stem}_{take + 1}" : stem;
    }

    /// <summary>Collision auto-suffixing across the batch: <c>name</c>, <c>name_2</c>, …
    /// (case-insensitive — the names also become DMX file names).</summary>
    private static string UniqueClipName(string name, HashSet<string> usedNames, bool autoSuffix)
    {
        if (usedNames.Add(name) || !autoSuffix)
            return name;
        for (var i = 2; ; i++)
        {
            var candidate = $"{name}_{i}";
            if (usedNames.Add(candidate))
                return candidate;
        }
    }

    private static string FileStem(string fileName)
    {
        var start = Math.Max(fileName.LastIndexOf('/'), fileName.LastIndexOf('\\')) + 1;
        var dot = fileName.LastIndexOf('.');
        var end = dot > start ? dot : fileName.Length;
        var stem = fileName.Substring(start, end - start);
        return stem.Length > 0 ? stem : "clip";
    }

    private static string ExtensionOf(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        if (dot < 0 || dot == fileName.Length - 1)
            return "";
        var ext = fileName.Substring(dot + 1);
        return ext.IndexOfAny(new[] { '/', '\\' }) >= 0 ? "" : ext.ToLowerInvariant();
    }

    private static string SanitizeFileName(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(
                c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-' ? c
                : c is >= 'A' and <= 'Z' ? char.ToLowerInvariant(c)
                : '_');
        }
        var sanitized = builder.ToString().Trim('_');
        return sanitized.Length > 0 ? sanitized : "clip";
    }

    private static string JoinAssetPath(string folder, string file)
    {
        var f = folder.Replace('\\', '/').TrimEnd('/');
        return f.Length == 0 ? file : f + "/" + file;
    }

    // ================================================================ target context

    /// <summary>Everything derived once per batch from the target rig: character up axis,
    /// foot chains, hips/root indices, IK-bone presence.</summary>
    internal sealed class TargetContext
    {
        public TargetRig Rig { get; }

        /// <summary>Target character up direction (midShoulders − midHips on the rest pose,
        /// via <see cref="CharacterFrame"/>); null when the rig lacks the bones for it.</summary>
        public Vector3? Up { get; }

        /// <summary>Target character frame (for the arm-IK basis change); null when not computable.</summary>
        public CharacterFrame? Frame { get; }

        /// <summary>Left/right leg chains from the rig's UpperLeg/LowerLeg/Foot/Toe roles;
        /// null when either leg is incompletely mapped.</summary>
        public (FootChain Left, FootChain Right)? FootChains { get; }

        /// <summary>Why <see cref="Up"/> / <see cref="FootChains"/> are unavailable (report note text).</summary>
        public string UpOrChainProblem { get; } = "";

        /// <summary>Target bone carrying the Hips role.</summary>
        public int? HipsIndex { get; }

        /// <summary>Dedicated translating root: a parentless Animated bone that is an
        /// ancestor of (and distinct from) the hips. Null on the s&amp;box rig (pelvis is
        /// itself parentless; root_IK is IkBaked).</summary>
        public int? DedicatedRootIndex { get; }

        /// <summary>True when the hips bone's direct parent is the dedicated root.</summary>
        public bool HipsParentIsRoot { get; }

        public bool HasIkBakedBones { get; }

        public TargetContext(TargetRig rig)
        {
            Rig = rig;
            HipsIndex = rig.BoneForRole(BoneRole.Hips);
            DedicatedRootIndex = FindDedicatedRoot(rig, HipsIndex);
            HipsParentIsRoot = HipsIndex is { } hips && DedicatedRootIndex is { } root
                && rig.Skeleton[hips].ParentIndex == root;
            foreach (var _ in rig.BonesOfClass(BoneClass.IkBaked))
            {
                HasIkBakedBones = true;
                break;
            }

            try
            {
                Frame = CharacterFrame.Compute(rig.Skeleton, RoleMap(rig), rig.Skeleton.RestWorld);
                Up = Frame.Up;
            }
            catch (ArgumentException e)
            {
                UpOrChainProblem = $"target character frame not computable ({e.Message})";
                return;
            }

            var left = LegChain(rig, BoneRole.UpperLegL, BoneRole.LowerLegL, BoneRole.FootL, BoneRole.ToeL);
            var right = LegChain(rig, BoneRole.UpperLegR, BoneRole.LowerLegR, BoneRole.FootR, BoneRole.ToeR);
            if (left is not null && right is not null)
                FootChains = (left, right);
            else
                UpOrChainProblem = "target rig does not map a complete UpperLeg/LowerLeg/Foot chain on both sides";
        }

        /// <summary>The rig's role annotations expressed as a <see cref="MappingResult"/>
        /// (what <see cref="CharacterFrame.Compute"/> consumes).</summary>
        public static MappingResult RoleMap(TargetRig rig)
        {
            var map = new MappingResult(rig.Name, MappingSource.Preset) { Confidence = 1f };
            for (var i = 0; i < rig.Skeleton.Count; i++)
            {
                if (rig.RoleOf(i) is { } role)
                    map.RoleToBone[role] = i;
            }
            return map;
        }

        private static FootChain? LegChain(TargetRig rig, BoneRole upper, BoneRole lower, BoneRole foot, BoneRole toe)
            => rig.BoneForRole(upper) is { } hip
               && rig.BoneForRole(lower) is { } knee
               && rig.BoneForRole(foot) is { } ankle
                ? new FootChain { Hip = hip, Knee = knee, Ankle = ankle, Toe = rig.BoneForRole(toe) }
                : null;

        private static int? FindDedicatedRoot(TargetRig rig, int? hipsIndex)
        {
            if (hipsIndex is not { } hips)
                return null;
            // Walk up from the hips; the topmost ancestor qualifies when it is a parentless
            // Animated bone distinct from the hips itself.
            var top = hips;
            while (rig.Skeleton[top].ParentIndex >= 0)
                top = rig.Skeleton[top].ParentIndex;
            return top != hips && rig.ClassOf(top) == BoneClass.Animated ? top : null;
        }
    }

    // ================================================================ arm effector IK

    /// <summary>
    /// Optional wrist-reach cleanup: per frame, the source hand offset from its shoulder is
    /// limb-length-normalized, re-expressed in the target's character basis, and used as a
    /// two-bone IK goal for the target arm (<see cref="EffectorIk.ApplyGoals"/>).
    /// </summary>
    private static class ArmIkCleanup
    {
        /// <summary>Runs both arms; returns a problem note (and does nothing) when the
        /// prerequisites are missing, null on success.</summary>
        public static string? Apply(
            List<XForm[]> frames, SourceScene scene, MappingResult map,
            TargetRig rig, TargetContext context, int take)
        {
            if (context.Frame is not { } targetFrame)
                return context.UpOrChainProblem;

            CharacterFrame sourceFrame;
            try
            {
                sourceFrame = CharacterFrame.Compute(scene.Skeleton, map, scene.Skeleton.RestWorld);
            }
            catch (ArgumentException e)
            {
                return $"source character frame not computable ({e.Message})";
            }

            // Change of basis source space → target space (this library's convention:
            // a * b applies b first).
            var sourceToTarget = MathQ.Normalize(
                BasisRotation(targetFrame) * Quaternion.Conjugate(BasisRotation(sourceFrame)));

            var appliedAny = false;
            appliedAny |= ApplyArm(frames, scene, map, rig, sourceToTarget, take,
                BoneRole.UpperArmL, BoneRole.LowerArmL, BoneRole.HandL);
            appliedAny |= ApplyArm(frames, scene, map, rig, sourceToTarget, take,
                BoneRole.UpperArmR, BoneRole.LowerArmR, BoneRole.HandR);
            return appliedAny ? null : "no complete UpperArm/LowerArm/Hand chain mapped on source and target";
        }

        private static bool ApplyArm(
            List<XForm[]> frames, SourceScene scene, MappingResult map, TargetRig rig,
            Quaternion sourceToTarget, int take, BoneRole upperRole, BoneRole lowerRole, BoneRole handRole)
        {
            if (!map.RoleToBone.TryGetValue(upperRole, out var srcUpper)
                || !map.RoleToBone.TryGetValue(lowerRole, out var srcLower)
                || !map.RoleToBone.TryGetValue(handRole, out var srcHand))
                return false;
            if (rig.BoneForRole(upperRole) is not { } tgtUpper
                || rig.BoneForRole(lowerRole) is not { } tgtLower
                || rig.BoneForRole(handRole) is not { } tgtHand)
                return false;

            var src = scene.Skeleton;
            var tgt = rig.Skeleton;
            var srcLen = Vector3.Distance(src.RestWorld[srcUpper].Pos, src.RestWorld[srcLower].Pos)
                       + Vector3.Distance(src.RestWorld[srcLower].Pos, src.RestWorld[srcHand].Pos);
            var tgtLen = Vector3.Distance(tgt.RestWorld[tgtUpper].Pos, tgt.RestWorld[tgtLower].Pos)
                       + Vector3.Distance(tgt.RestWorld[tgtLower].Pos, tgt.RestWorld[tgtHand].Pos);
            if (srcLen < 1e-4f || tgtLen < 1e-4f)
                return false;
            var scale = tgtLen / srcLen;

            var sourceFrames = scene.Clips[take].Frames;
            var count = Math.Min(frames.Count, sourceFrames.Count);
            var goals = new Vector3[frames.Count];
            for (var f = 0; f < frames.Count; f++)
            {
                var srcWorld = new Pose(sourceFrames[Math.Min(f, count - 1)]).ToWorld(src);
                var tgtWorld = new Pose(frames[f]).ToWorld(tgt);
                var reach = srcWorld[srcHand].Pos - srcWorld[srcUpper].Pos;
                goals[f] = tgtWorld[tgtUpper].Pos + Vector3.Transform(reach, sourceToTarget) * scale;
            }

            EffectorIk.ApplyGoals(
                frames, tgt,
                new LimbChain { Upper = tgtUpper, Lower = tgtLower, End = tgtHand },
                goals, RestBendAxis(tgt, tgtUpper, tgtLower, tgtHand));
            return true;
        }

        /// <summary>Hinge-axis fallback from the rest bend plane (elbow), like the foot pass.</summary>
        private static Vector3 RestBendAxis(SkeletonModel skeleton, int upper, int lower, int end)
        {
            var a = skeleton.RestWorld[upper].Pos;
            var b = skeleton.RestWorld[lower].Pos;
            var c = skeleton.RestWorld[end].Pos;
            var axis = Vector3.Cross(c - a, b - a);
            if (axis.LengthSquared() > 1e-6f)
                return Vector3.Normalize(axis);
            var limb = c - a;
            axis = Vector3.Cross(limb, Vector3.UnitY);
            if (axis.LengthSquared() < 1e-6f)
                axis = Vector3.Cross(limb, Vector3.UnitZ);
            return axis.LengthSquared() > 1e-12f ? Vector3.Normalize(axis) : Vector3.UnitX;
        }

        /// <summary>Rotation taking canonical character axes (X=lateral, Y=up, Z=forward)
        /// to the rig's world axes.</summary>
        private static Quaternion BasisRotation(CharacterFrame frame)
        {
            var m = new Matrix4x4(
                frame.Lateral.X, frame.Lateral.Y, frame.Lateral.Z, 0f,
                frame.Up.X, frame.Up.Y, frame.Up.Z, 0f,
                frame.Forward.X, frame.Forward.Y, frame.Forward.Z, 0f,
                0f, 0f, 0f, 1f);
            return MathQ.Normalize(Quaternion.CreateFromRotationMatrix(m));
        }
    }
}
