using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using HumanoidRetargeter.Cleanup;
using HumanoidRetargeter.Formats;
using HumanoidRetargeter.Formats.Bvh;
using HumanoidRetargeter.Formats.Dmx;
using HumanoidRetargeter.Formats.Fbx;
using HumanoidRetargeter.Formats.Gltf;
using HumanoidRetargeter.Formats.Renderware;
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
/// <item><b>Import</b> — format by file extension (<c>.fbx</c>/<c>.bvh</c>/<c>.glb</c>/
/// <c>.gltf</c>/<c>.vrm</c>/<c>.anm</c>/<c>.an5</c>), content sniff as fallback →
/// <see cref="SourceScene"/> (cm, native axes). RenderWare animations additionally take the
/// companion model's .dff bytes via <see cref="RetargetRequest.SkeletonData"/>.</item>
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
    /// <summary>Converts one source file — all takes, or only
    /// <see cref="RetargetRequest.TakeIndex"/> when set. Equivalent to a one-request batch.</summary>
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

        // Augment mode: names already taken in the existing vmdl's AnimationList must not be
        // silently repointed — seed the collision set with every existing node name EXCEPT
        // our own AnimFiles (source_filename under DmxFolderRelative), which are replaceable
        // so re-running the same batch stays idempotent.
        if (options.AugmentVmdlText is not null)
            SeedUsedNamesFromExistingVmdl(options.AugmentVmdlText, options.DmxFolderRelative, usedNames);

        var mappedPinky = false;
        foreach (var request in requests)
        {
            if (request is null)
                continue;
            mappedPinky |= ProcessRequest(
                request, target, context, options, usedNames, result.Clips, entries);
        }

        foreach (var clip in result.Clips)
        {
            if (!clip.Success)
                result.Errors.Add($"{clip.SourceFileName}: {clip.Error}");
        }

        // Locomotion families are detected on the FINAL (collision-suffixed) clip names so
        // the blend grids reference exactly what the vmdl registers; folder/blend names are
        // made unique against the same name set the clips used.
        IReadOnlyList<LocomotionSetSpec>? locomotionSets = null;
        if (options.DetectLocomotionSets)
        {
            var (sets, reports) = LocomotionSetDetector.Detect(
                entries, usedNames, options.AutoSuffixCollisions);
            result.LocomotionSets.AddRange(reports);
            if (sets.Count > 0)
                locomotionSets = sets;
        }

        result.StandaloneVmdl = VmdlWriter.GenerateStandalone(
            target.BaseModelPath, entries, target.VmdlScale, target.DefaultRootBone,
            locomotionSets, target.MeshFilePath, target.MeshImportScale);

        if (options.AugmentVmdlText is not null)
        {
            try
            {
                var removedSequences = new List<string>();
                result.AugmentedVmdl = VmdlAugmenter.Augment(
                    options.AugmentVmdlText, entries, out _,
                    new AugmentOptions
                    {
                        DefaultRootBone = target.DefaultRootBone,
                        // The citizen base model copies ring→pinky via its CopyPinky
                        // constraints; when this batch actually drives the pinky, the
                        // augmented vmdl must neutralize them or the exported pinky
                        // channels get overridden at runtime.
                        NeutralizePinkyConstraints = mappedPinky,
                        LocomotionSets = locomotionSets,
                        // Same ownership rule the name seeding above used: pipeline-owned
                        // locomotion folders (every AnimFile sourced under our DMX folder)
                        // are replaceable wholesale on re-runs.
                        DmxFolderRelative = options.DmxFolderRelative,
                        // Stale entries whose DMX the caller found missing on disk: keep
                        // them and the WHOLE vmdl stops compiling ("Node 'X' resolve
                        // failure"), new sequences included.
                        MissingSourceFiles = options.MissingAnimSources,
                    },
                    removedSequences);
                foreach (var name in removedSequences)
                {
                    result.Warnings.Add(
                        $"removed stale sequence '{name}' from the augmented vmdl: its "
                        + "animation source file no longer exists on disk (a missing source "
                        + "fails the whole model recompile)");
                }
            }
            catch (Exception e)
            {
                result.Errors.Add($"vmdl augmentation failed: {e.Message}");
            }
        }

        return result;
    }

    /// <summary>Collects names already present in the existing vmdl's AnimationList that the
    /// batch must not reuse (everything except our own replaceable AnimFiles).</summary>
    private static void SeedUsedNamesFromExistingVmdl(
        string vmdlText, string dmxFolderRelative, HashSet<string> usedNames)
    {
        Kv3Document doc;
        try
        {
            doc = Kv3.Parse(vmdlText);
        }
        catch (FormatException)
        {
            return; // the augmentation step itself surfaces the parse error
        }

        if (doc.Root is not KvObject root || root.GetOrNull("rootNode") is not KvObject rootNode
            || rootNode.GetOrNull("children") is not KvArray children)
            return;
        var animList = children.Items.OfType<KvObject>()
            .FirstOrDefault(o => o.GetString("_class") == "AnimationList");
        if (animList?.GetOrNull("children") is not KvArray items)
            return;

        var folder = dmxFolderRelative.Replace('\\', '/').TrimEnd('/');
        SeedNames(items, folder, usedNames);
    }

    /// <summary>
    /// Recursive seeding step: every named AnimationList node reserves its name EXCEPT our
    /// own replaceable output — AnimFiles whose source lives in the batch's DMX folder, and
    /// locomotion Folders consisting entirely of such AnimFiles (a re-run replaces the whole
    /// folder, so neither its name nor its content may block the new batch's names). Foreign
    /// folders are seeded by name and their content seeded recursively.
    /// </summary>
    private static void SeedNames(KvArray items, string dmxFolder, HashSet<string> usedNames)
    {
        foreach (var node in items.Items.OfType<KvObject>())
        {
            var name = node.GetString("name");
            var cls = node.GetString("_class");

            if (cls == "AnimFile")
            {
                if (!string.IsNullOrEmpty(name) && !IsOurAnimFile(node, dmxFolder))
                    usedNames.Add(name);
                continue;
            }

            if (cls == "Folder")
            {
                if (IsOurLocomotionFolder(node, dmxFolder))
                    continue; // replaceable wholesale — nothing inside reserves a name
                if (!string.IsNullOrEmpty(name))
                    usedNames.Add(name);
                if (node.GetOrNull("children") is KvArray nested)
                    SeedNames(nested, dmxFolder, usedNames);
                continue;
            }

            if (!string.IsNullOrEmpty(name))
                usedNames.Add(name);
        }
    }

    /// <summary>Whether an AnimFile was written by this pipeline: its source_filename sits
    /// inside the batch's DMX folder.</summary>
    private static bool IsOurAnimFile(KvObject node, string dmxFolder)
    {
        var source = (node.GetString("source_filename") ?? "").Replace('\\', '/');
        return dmxFolder.Length == 0
            ? !source.Contains('/')
            : source.StartsWith(dmxFolder + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether a Folder is a locomotion group this pipeline spliced: it contains at
    /// least one AnimFile and every AnimFile inside it (recursively) is ours.</summary>
    private static bool IsOurLocomotionFolder(KvObject folderNode, string dmxFolder)
    {
        var sawAnimFile = false;
        return Walk(folderNode) && sawAnimFile;

        bool Walk(KvObject node)
        {
            if (node.GetString("_class") == "AnimFile")
            {
                sawAnimFile = true;
                return IsOurAnimFile(node, dmxFolder);
            }
            if (node.GetOrNull("children") is KvArray children)
            {
                foreach (var child in children.Items.OfType<KvObject>())
                {
                    if (!Walk(child))
                        return false;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// Detection-only entry point for UI listings: imports the file and runs the same
    /// preset-then-auto mapping as conversion, without solving anything. Also reports the
    /// file's take metadata (clip names, in take-index order) so listings can expand a
    /// multi-take file into one entry per take. Throws <see cref="FormatException"/> when
    /// the file is unreadable.
    /// </summary>
    public static InspectResult Inspect(byte[] sourceData, string fileName, byte[]? skeletonData = null)
    {
        ArgumentNullException.ThrowIfNull(sourceData);
        ArgumentNullException.ThrowIfNull(fileName);
        var scene = ImportSource(sourceData, fileName, skeletonData: skeletonData);
        var takeNames = new List<string>(scene.Clips.Count);
        foreach (var clip in scene.Clips)
            takeNames.Add(clip.Name);
        return new InspectResult
        {
            Mapping = ResolveMapping(scene.Skeleton, authoredMapping: scene.AuthoredMapping).Report,
            TakeNames = takeNames,
        };
    }

    // ================================================================ per-request pipeline

    /// <summary>Runs one request end to end. Returns true when at least one clip converted
    /// successfully with a mapping that drives a pinky role (CopyPinky handling).</summary>
    private static bool ProcessRequest(
        RetargetRequest request, RetargetTargetSpec target, TargetContext context,
        BatchOptions options, HashSet<string> usedNames,
        List<ClipResult> clips, List<AnimEntry> entries)
    {
        var sourceId = request.SourceId ?? request.SourceFileName;
        SourceScene scene;
        MappingReportInfo report;
        MappingResult map;
        try
        {
            scene = ImportSource(
                request.SourceData, request.SourceFileName, request.SampleFps, request.SkeletonData);
            (map, report) = ResolveMapping(
                scene.Skeleton, request.MappingOverride, authoredMapping: scene.AuthoredMapping);
        }
        catch (Exception e)
        {
            clips.Add(new ClipResult
            {
                ClipName = FileStem(request.SourceFileName),
                SourceFileName = request.SourceFileName,
                SourceId = sourceId,
                Success = false,
                Error = e.Message,
            });
            return false;
        }

        if (scene.Clips.Count == 0)
        {
            clips.Add(new ClipResult
            {
                ClipName = FileStem(request.SourceFileName),
                SourceFileName = request.SourceFileName,
                SourceId = sourceId,
                Mapping = report,
                Success = false,
                Error = "Source file contains no animation takes.",
            });
            return false;
        }

        var mapsPinky = MapsPinkyRole(map);
        if (mapsPinky && options.AugmentVmdlText is null
            && target.BaseModelPath == RetargetTargetSpec.SboxHumanMalePath)
        {
            // A standalone child vmdl cannot override the base model's AnimConstraintList:
            // the citizen's CopyPinky (ring → pinky orient copy) keeps driving the pinky at
            // runtime even though the DMX carries real pinky channels.
            AddNote(report,
                "Pinky channels are exported, but the base model's CopyPinky constraints "
                + "(ring → pinky copy) cannot be overridden from a standalone vmdl — use "
                + "augment mode (add to the existing vmdl) to neutralize them.");
        }
        if (target.UpAxis == TargetUpAxis.ZUpEngine)
        {
            AddNote(report,
                "Target rig is engine-space (Z-up, inches): the DMX declares a Z-up axis "
                + "system so resourcecompiler performs no Y-up conversion (best-effort — "
                + "verify the compiled sequence on engine-space targets).");
        }

        // External clip definitions (Unity .fbx.meta clipAnimations): one output clip per
        // DEFINITION, sliced out of its take; TakeIndex addresses the definitions then
        // (see RetargetRequest.ClipDefinitions).
        if (request.ClipDefinitions is { Count: > 0 } clipDefs)
        {
            return ProcessClipDefinitions(
                request, target, context, options, usedNames, clips, entries,
                scene, map, report, sourceId, mapsPinky, clipDefs);
        }

        // TakeIndex narrows the conversion to a single take (UI per-take entries submit one
        // request per selected take); null keeps the historical convert-all-takes behavior.
        var takeStart = 0;
        var takeEnd = scene.Clips.Count;
        if (request.TakeIndex is { } takeIndex)
        {
            if (takeIndex < 0 || takeIndex >= scene.Clips.Count)
            {
                clips.Add(new ClipResult
                {
                    ClipName = FileStem(request.SourceFileName),
                    SourceFileName = request.SourceFileName,
                    SourceId = sourceId,
                    Mapping = report,
                    Success = false,
                    Error = $"Take index {takeIndex} is out of range: the source file "
                        + $"contains {scene.Clips.Count} take(s).",
                });
                return false;
            }
            takeStart = takeIndex;
            takeEnd = takeIndex + 1;
        }

        var anyPinkySuccess = false;
        for (var take = takeStart; take < takeEnd; take++)
        {
            var clipName = UniqueClipName(
                SanitizeClipName(RequestedClipName(request, scene, take)),
                usedNames, options.AutoSuffixCollisions);
            anyPinkySuccess |= ConvertOne(
                request, target, context, options, usedNames, clips, entries,
                scene, map, report, sourceId, mapsPinky, take, clipName);
        }

        return anyPinkySuccess;
    }

    /// <summary>
    /// The definitions variant of the take loop: each <see cref="ExternalClipDef"/> becomes
    /// its own clip result — the definition's take is located by
    /// <see cref="ExternalClipDef.TakeName"/> (falling back to the file's first take), sliced
    /// to the definition's native-frame range via <see cref="UnityMeta.Slice"/> and solved as
    /// a single-clip scene. Returns true when at least one clip converted successfully with a
    /// pinky-driving mapping.
    /// </summary>
    private static bool ProcessClipDefinitions(
        RetargetRequest request, RetargetTargetSpec target, TargetContext context,
        BatchOptions options, HashSet<string> usedNames,
        List<ClipResult> clips, List<AnimEntry> entries,
        SourceScene scene, MappingResult map, MappingReportInfo report,
        string sourceId, bool mapsPinky, IReadOnlyList<ExternalClipDef> clipDefs)
    {
        var defStart = 0;
        var defEnd = clipDefs.Count;
        if (request.TakeIndex is { } defIndex)
        {
            if (defIndex < 0 || defIndex >= clipDefs.Count)
            {
                clips.Add(new ClipResult
                {
                    ClipName = FileStem(request.SourceFileName),
                    SourceFileName = request.SourceFileName,
                    SourceId = sourceId,
                    Mapping = report,
                    Success = false,
                    Error = $"Clip-definition index {defIndex} is out of range: the request "
                        + $"carries {clipDefs.Count} clip definition(s).",
                });
                return false;
            }
            defStart = defIndex;
            defEnd = defIndex + 1;
        }

        var anyPinkySuccess = false;
        for (var d = defStart; d < defEnd; d++)
        {
            var def = clipDefs[d];
            var requestedName = !string.IsNullOrWhiteSpace(def.Name)
                ? def.Name
                : clipDefs.Count > 1 ? $"{FileStem(request.SourceFileName)}_{d + 1}" : FileStem(request.SourceFileName);
            var clipName = UniqueClipName(
                SanitizeClipName(requestedName), usedNames, options.AutoSuffixCollisions);

            var take = TakeForDefinition(scene, def);
            var sliced = UnityMeta.Slice(scene.Clips[take], def, clipName);

            // Same skeleton/axes, ONE clip: the regular pipeline then solves "take 0" of it.
            var defScene = new SourceScene(
                scene.Skeleton, new[] { sliced }, scene.UnitScaleCm,
                scene.UpAxis, scene.UpAxisSign,
                scene.FrontAxis, scene.FrontAxisSign,
                scene.CoordAxis, scene.CoordAxisSign,
                scene.OriginalUpAxis, scene.Notes)
            {
                AuthoredMapping = scene.AuthoredMapping,
                RestPlacementAuthored = scene.RestPlacementAuthored,
            };

            anyPinkySuccess |= ConvertOne(
                request, target, context, options, usedNames, clips, entries,
                defScene, map, report, sourceId, mapsPinky, take: 0, clipName);
        }

        return anyPinkySuccess;
    }

    /// <summary>The take a definition's frame range refers to: matched by take name when the
    /// definition records one (Unity's <c>takeName</c>, e.g. <c>root|Animation</c>), else —
    /// and when nothing matches — the file's first take.</summary>
    private static int TakeForDefinition(SourceScene scene, ExternalClipDef def)
    {
        if (!string.IsNullOrWhiteSpace(def.TakeName))
        {
            for (var i = 0; i < scene.Clips.Count; i++)
            {
                if (string.Equals(scene.Clips[i].Name, def.TakeName, StringComparison.Ordinal))
                    return i;
            }
        }
        return 0;
    }

    /// <summary>Solves ONE clip (take <paramref name="take"/> of <paramref name="scene"/>)
    /// end to end — solve + cleanup + DMX, plus the optional mirrored twin
    /// (<see cref="RetargetRequest.CreateMirroredVariant"/>) — appending success or failure
    /// <see cref="ClipResult"/>s (failures never abort the batch). Returns true on success
    /// with a pinky-driving mapping.</summary>
    private static bool ConvertOne(
        RetargetRequest request, RetargetTargetSpec target, TargetContext context,
        BatchOptions options, HashSet<string> usedNames,
        List<ClipResult> clips, List<AnimEntry> entries,
        SourceScene scene, MappingResult map, MappingReportInfo report,
        string sourceId, bool mapsPinky, int take, string clipName)
    {
        Clip clip;
        try
        {
            clip = SolveAndClean(request, target, context, scene, map, report, take, clipName);
            EmitClip(request, target, context, options, usedNames, clips, entries, report,
                sourceId, clipName, clip.Frames, clip.Fps, clip.Looping, isMirrored: false);
        }
        catch (Exception e)
        {
            clips.Add(new ClipResult
            {
                ClipName = clipName,
                SourceFileName = request.SourceFileName,
                SourceId = sourceId,
                Mapping = report,
                Success = false,
                Error = e.Message,
            });
            return false;
        }

        if (request.CreateMirroredVariant)
        {
            // The twin gets its own collision-suffixed name and its own failure isolation:
            // a mirror problem (asymmetric rig) fails ONLY the twin, never the primary clip.
            var mirroredName = UniqueClipName(
                SanitizeClipName(clipName + "_M"), usedNames, options.AutoSuffixCollisions);
            try
            {
                var mirroredFrames = ClipMirror.Mirror(clip.Frames, target.Rig);
                // IK helper bones derive from the body: re-bake them from the MIRRORED body
                // (their mirrored channels are placeholders the baker overwrites).
                if (context.HasIkBakedBones)
                    IkBoneBaker.Bake(mirroredFrames, target.Rig);
                EmitClip(request, target, context, options, usedNames, clips, entries, report,
                    sourceId, mirroredName, mirroredFrames, clip.Fps, clip.Looping, isMirrored: true);
            }
            catch (Exception e)
            {
                clips.Add(new ClipResult
                {
                    ClipName = mirroredName,
                    SourceFileName = request.SourceFileName,
                    SourceId = sourceId,
                    Mapping = report,
                    Success = false,
                    Error = $"mirrored variant: {e.Message}",
                });
            }
        }

        return mapsPinky;
    }

    /// <summary>Shared tail of clip production (primary and mirrored twin): optional footstep
    /// events, DMX serialization, the <see cref="ClipResult"/> and the vmdl
    /// <see cref="AnimEntry"/> — plus the additive (<c>_delta</c>) companion entry when
    /// <see cref="RetargetRequest.CreateAdditiveVariant"/> is on (a second AnimFile REUSING
    /// the clip's DMX with an AnimSubtract child, shipped-citizen shape; no separate
    /// <see cref="ClipResult"/> since no separate DMX exists).</summary>
    private static void EmitClip(
        RetargetRequest request, RetargetTargetSpec target, TargetContext context,
        BatchOptions options, HashSet<string> usedNames,
        List<ClipResult> clips, List<AnimEntry> entries,
        MappingReportInfo report, string sourceId,
        string clipName, List<XForm[]> frames, float fps, bool looping, bool isMirrored)
    {
        var events = GenerateFootsteps(request, target, context, report, frames, fps);
        var dmxFileName = SanitizeFileName(clipName) + ".dmx";
        var dmx = DmxWriter.Write(
            target.Rig.Skeleton, new Clip(clipName, fps, looping, frames), new DmxWriteOptions
            {
                Name = clipName,
                SourceNote = isMirrored ? request.SourceFileName + " (mirrored)" : request.SourceFileName,
                // Design §3: ConstraintDriven (twist/helper) bones keep their joints +
                // bind in the DMX but get NO channels — the model's AnimConstraintList
                // drives them. Face bones are exempt from the exclusion (rest-local
                // channels): nothing drives them in a compiled sequence, and channel-less
                // face joints bake statically (eyes out of sockets in ModelDoc).
                ChannelExcludedBones = context.ConstraintDrivenBones,
                UpAxisY = target.UpAxis == TargetUpAxis.YUpCm,
            });

        var extractMotion = request.RootMotion == RootMotionMode.Extract;

        // Additive variant: '<clip>_delta' (shipped naming), collision-suffixed like every
        // other batch name. Only the NAME is produced here — the vmdl entry below reuses
        // the base clip's DMX (resourcecompiler does the reference-frame subtraction).
        var deltaName = request.CreateAdditiveVariant
            ? UniqueClipName(
                SanitizeClipName(clipName + "_delta"), usedNames, options.AutoSuffixCollisions)
            : null;

        clips.Add(new ClipResult
        {
            ClipName = clipName,
            SourceFileName = request.SourceFileName,
            SourceId = sourceId,
            DmxFileName = dmxFileName,
            DmxContent = dmx,
            Mapping = report,
            Success = true,
            SolvedFrames = frames,
            Fps = fps,
            Looping = looping,
            ExtractMotion = extractMotion,
            FootstepEvents = events,
            IsMirroredVariant = isMirrored,
            HasAdditiveVariant = deltaName is not null,
            AdditiveVariantName = deltaName,
        });
        var sourceFilename = JoinAssetPath(options.DmxFolderRelative, dmxFileName);
        entries.Add(new AnimEntry
        {
            Name = clipName,
            SourceFilename = sourceFilename,
            Looping = looping,
            ExtractMotion = extractMotion,
            Events = events,
        });
        if (deltaName is not null)
        {
            // The shipped _delta sequences carry the AnimSubtract child and nothing else
            // (no motion extraction, no events) — an additive layer fires no footsteps and
            // extracting root motion from a delta makes no sense.
            entries.Add(new AnimEntry
            {
                Name = deltaName,
                SourceFilename = sourceFilename,
                Looping = looping,
                SubtractAnimName = clipName,
                SubtractFrame = 0,
            });
        }
    }

    /// <summary>
    /// Generates the <c>AE_FOOTSTEP</c> events for a solved clip when the request asks for
    /// them (<see cref="RetargetRequest.GenerateFootstepEvents"/>): plant intervals are
    /// detected on the SOLVED TARGET frames (this is where the engine plays the clip, so
    /// touchdowns must be measured here), each interval start = one footstep
    /// (<see cref="FootstepEvents"/>). Empty when the feature is off or the target rig lacks
    /// the leg chains / character up (noted on the report then).
    /// </summary>
    private static IReadOnlyList<AnimEventEntry> GenerateFootsteps(
        RetargetRequest request, RetargetTargetSpec target, TargetContext context,
        MappingReportInfo report, List<XForm[]> frames, float fps)
    {
        if (!request.GenerateFootstepEvents)
            return Array.Empty<AnimEventEntry>();
        if (context.Up is not { } up || context.FootChains is not { } feet)
        {
            AddNote(report, "Footstep events skipped: " + context.UpOrChainProblem);
            return Array.Empty<AnimEventEntry>();
        }
        return FootstepEvents.Generate(
            frames, target.Rig.Skeleton, feet.Left, feet.Right, up, fps,
            ScaledPlantOptions(target));
    }

    /// <summary>Default plant thresholds are cm-tuned; engine-space rigs are in inches, so
    /// they are scaled by the cm→inch factor to keep the same physical sensitivity (shared by
    /// the foot-plant cleanup and the footstep-event detection).</summary>
    private static FootPlantOptions ScaledPlantOptions(RetargetTargetSpec target)
    {
        var options = new FootPlantOptions();
        if (target.UpAxis == TargetUpAxis.ZUpEngine)
        {
            options.SpeedThresholdCmPerSec *= RetargetTargetSpec.SboxSourceScale;
            options.HeightThresholdCm *= RetargetTargetSpec.SboxSourceScale;
        }
        return options;
    }

    private static bool MapsPinkyRole(MappingResult map)
    {
        foreach (var role in map.RoleToBone.Keys)
        {
            if (role.ToString().StartsWith("Pinky", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>Solve one take and run the target-space cleanup + IK baking passes.</summary>
    private static Clip SolveAndClean(
        RetargetRequest request, RetargetTargetSpec target, TargetContext context,
        SourceScene scene, MappingResult map, MappingReportInfo report, int take, string clipName)
    {
        var requested = request.Solve ?? new SolveOptions();
        var solver = ResolveSolver(request, target, context, report);
        var solved = solver.Solve(scene, map, target.Rig, new SolveOptions
        {
            HipScaleHorizontal = requested.HipScaleHorizontal,
            HipScaleVertical = requested.HipScaleVertical,
            TransferFingers = requested.TransferFingers,
            TransferModes = requested.TransferModes,
            ClipIndex = take,
            ClipName = clipName,
        });
        var frames = solved.Frames;

        // ---- foot-plant cleanup (target space; up axis from the TARGET character frame) ----
        if (request.FootPlantCleanup)
        {
            if (context.Up is { } up && context.FootChains is { } feet)
            {
                // Grounded stance alignment first: levels planted soles against the ground
                // (removes the stance offset a non-stance source rest leaves in the solver's
                // rest-relative foot transfer). Plants are detected on the SOURCE clip —
                // ground truth; hip-height rescaling can push the solved target trajectories
                // outside the cm-tuned Kovar thresholds. Composes with the position pinning
                // below (this rotates feet about their own joints; the pinning preserves
                // foot world rotations).
                GroundAlignFeet(frames, scene, map, target.Rig.Skeleton, feet, up, solved.Fps, take);

                FootPlant.Apply(
                    frames, target.Rig.Skeleton, feet.Left, feet.Right, up, solved.Fps,
                    ScaledPlantOptions(target));
            }
            else
            {
                AddNote(report, "Foot-plant cleanup skipped: " + context.UpOrChainProblem);
            }
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

    /// <summary>
    /// Picks the solver for a request (design §10 routing): the
    /// <see cref="GeometricSolver"/> unless the request selects
    /// <see cref="SolverKind.DeepLearning"/>, which needs the spec's
    /// <see cref="RetargetTargetSpec.DlWeights"/> and is built once per batch (the parsed
    /// model is cached on the <see cref="TargetContext"/>). A DL request also notes that
    /// per-role mapping is ignored by that solver.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown (and surfaced as the clip's
    /// error) when DL is requested without weights.</exception>
    private static IRetargetSolver ResolveSolver(
        RetargetRequest request, RetargetTargetSpec target, TargetContext context,
        MappingReportInfo report)
    {
        if (request.Solver != SolverKind.DeepLearning)
            return new GeometricSolver();

        if (target.DlWeights is not { Length: > 0 })
        {
            throw new InvalidOperationException(
                "Deep-learning solver requested but RetargetTargetSpec.DlWeights is not set "
                + "(read Assets/humanoid_retargeter/dl/same_v1.weights and pass its bytes).");
        }

        AddNote(report,
            "Deep-learning solver (SAME, experimental): skeleton-agnostic — per-role mapping "
            + "is not used (hips/alignment heuristics only); finger bones stay at rest.");
        return context.GetDlSolver(target.DlWeights);
    }

    /// <summary>
    /// Normalized-rest foot pitch beyond which (toe approaching/above ankle level) the rest
    /// cannot be a flat stance — measured stance rests sit at −11°…−51°; the repro artifact
    /// rig measured +2.7° on one foot.
    /// </summary>
    private const float StancePitchMaxDeg = -5f;

    /// <summary>Left/right normalized-rest foot pitch asymmetry beyond which the rest is not
    /// a (symmetric) stance — measured stance rests differ ≤ 2.4°; the repro artifact rig
    /// 13.8°.</summary>
    private const float StancePitchAsymmetryDeg = 6f;

    /// <summary>
    /// The grounded-foot stance recalibration step (<see cref="FootGroundAlign"/>). Runs ONLY
    /// when the source's NORMALIZED rest — the solver's delta reference — is implausible as a
    /// flat stance (a foot's toe at/above ankle level, or the two feet resting asymmetrically;
    /// both measured on the repro rig, where T-pose normalization of a bent-leg rest swings
    /// the feet 14–26° away from the rig's true stance). On plausible stance rests the solver's
    /// rest-relative foot transfer is already faithful, and planted-sole deviations are
    /// genuine articulation (boxing stances, heel rolls) that must NOT be flattened. Plant
    /// intervals are detected on the SOURCE clip (cm space, default Kovar thresholds).
    /// Best-effort — silently skipped when the source maps no complete leg chains + toes or a
    /// character frame is not computable (the foot transfer is then simply left as solved).
    /// </summary>
    private static void GroundAlignFeet(
        List<XForm[]> frames, SourceScene scene, MappingResult map, SkeletonModel targetSkeleton,
        (FootChain Left, FootChain Right) targetFeet, Vector3 targetUp, float fps, int take)
    {
        FootChain? SourceChain(BoneRole upper, BoneRole lower, BoneRole foot, BoneRole toe)
            => map.RoleToBone.TryGetValue(upper, out var hip)
               && map.RoleToBone.TryGetValue(lower, out var knee)
               && map.RoleToBone.TryGetValue(foot, out var ankle)
                ? new FootChain
                {
                    Hip = hip,
                    Knee = knee,
                    Ankle = ankle,
                    Toe = map.RoleToBone.TryGetValue(toe, out var t) ? t : null,
                }
                : null;

        var srcLeft = SourceChain(BoneRole.UpperLegL, BoneRole.LowerLegL, BoneRole.FootL, BoneRole.ToeL);
        var srcRight = SourceChain(BoneRole.UpperLegR, BoneRole.LowerLegR, BoneRole.FootR, BoneRole.ToeR);
        if (srcLeft?.Toe is not int srcToeL || srcRight?.Toe is not int srcToeR)
            return; // toe-less sources take the solver's virtual-foot fallback instead

        // ---- rest-stance plausibility (normalized rest, the solver's delta reference) ----
        float pitchL, pitchR;
        Vector3 srcUp;
        try
        {
            var (srcNorm, _) = RestNormalizer.Normalize(scene.Skeleton, map);
            var srcCanon = CanonicalFrames.Build(scene.Skeleton, map, srcNorm.WorldRest);
            srcUp = Vector3.Normalize(srcCanon.CharacterUp);

            float RestPitchDeg(int foot, int toe)
            {
                var dir = srcNorm.WorldRest[toe].Pos - srcNorm.WorldRest[foot].Pos;
                if (dir.LengthSquared() < 1e-8f)
                    return 0f;
                var s = Vector3.Dot(Vector3.Normalize(dir), srcUp);
                return MathF.Asin(Math.Clamp(s, -1f, 1f)) * (180f / MathF.PI);
            }

            pitchL = RestPitchDeg(srcLeft.Ankle, srcToeL);
            pitchR = RestPitchDeg(srcRight.Ankle, srcToeR);
        }
        catch (ArgumentException)
        {
            return;
        }

        var restIsStance = pitchL < StancePitchMaxDeg && pitchR < StancePitchMaxDeg
            && MathF.Abs(pitchL - pitchR) <= StancePitchAsymmetryDeg;
        if (restIsStance)
            return;

        var srcFrames = scene.Clips[take].Frames;
        var (plantsL, plantsR) = FootPlant.DetectPlantIntervals(
            srcFrames, scene.Skeleton, srcLeft, srcRight, srcUp, fps);
        if (plantsL.Count == 0 && plantsR.Count == 0)
            return;

        FootGroundAlign.Apply(
            frames, targetSkeleton, targetFeet.Left, targetFeet.Right, targetUp,
            plantsL, plantsR);
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
        // Skeleton-aware overload: hips world via real parent-chain FK, locals re-derived
        // against the actual parent (HipsParentIsRoot is ignored by this overload).
        RootMotion.Apply(frames, context.Rig.Skeleton, new RootMotionAxes
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

    /// <summary>
    /// Imports source-file bytes exactly like conversion does: the extension picks the
    /// importer; unknown extensions fall back to content sniffing (FBX binary magic / ASCII
    /// header token / BVH "HIERARCHY" / GLB 'glTF' magic / glTF JSON). Public so UI listings
    /// inspect files through the SAME import path the pipeline uses (no duplicate sniffing
    /// logic caller-side).
    /// </summary>
    /// <param name="data">Raw file bytes.</param>
    /// <param name="fileName">File name; only the extension is consulted.</param>
    /// <param name="sampleFps">Resample rate for the imported clips; null = importer default
    /// (30 fps).</param>
    /// <param name="skeletonData">Companion skeleton bytes for animation-only formats
    /// (RenderWare .anm/.an5 need the model .dff — see
    /// <see cref="RetargetRequest.SkeletonData"/>); ignored by self-contained formats.</param>
    /// <exception cref="FormatException">Thrown when the bytes are not a readable
    /// FBX/BVH/glTF/RenderWare animation.</exception>
    public static SourceScene ImportSource(
        byte[] data, string fileName, float? sampleFps = null, byte[]? skeletonData = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(fileName);

        var fbxOptions = sampleFps is { } fbxFps ? new FbxImportOptions { SampleFps = fbxFps } : null;
        var bvhOptions = sampleFps is { } bvhFps ? new BvhImportOptions { SampleFps = bvhFps } : null;
        var gltfOptions = sampleFps is { } gltfFps ? new GltfImportOptions { SampleFps = gltfFps } : null;
        // RenderWare banks carry no clip names — takes are named from the file stem.
        var rwOptions = new RwAnmImportOptions
        {
            SampleFps = sampleFps ?? 30f,
            ClipNameBase = FileStem(fileName),
        };
        var ext = ExtensionOf(fileName);
        return ext switch
        {
            "fbx" => FbxImporter.Import(data, fbxOptions),
            "bvh" => BvhImporter.Import(data, bvhOptions),
            // .vrm = glTF 2.0 GLB container + VRM extension (the importer reads the authored
            // humanoid bone map into SourceScene.AuthoredMapping); unknown-extension VRM
            // bytes also land here via the GLB magic sniff below.
            "glb" or "gltf" or "vrm" => GltfImporter.Import(data, gltfOptions),
            // RenderWare animations (FSB2 .anm single clips / .an5 banks); the skeleton
            // comes from the companion model .dff (a missing skeleton throws the
            // importer's instructive error).
            "anm" or "an5" => RwAnmImporter.Import(data, skeletonData, rwOptions),
            _ => SniffFormat(data) switch
            {
                "fbx" => FbxImporter.Import(data, fbxOptions),
                "bvh" => BvhImporter.Import(data, bvhOptions),
                "gltf" => GltfImporter.Import(data, gltfOptions),
                "rwanim" => RwAnmImporter.Import(data, skeletonData, rwOptions),
                _ => throw new FormatException(
                    $"Unrecognized source format for '{fileName}' (expected .fbx, .bvh, .glb, .gltf, .vrm, .anm or .an5)."),
            },
        };
    }

    private static string? SniffFormat(byte[] data)
    {
        // Binary FBX magic: "Kaydara FBX Binary  \0".
        const string fbxMagic = "Kaydara FBX Binary";
        if (StartsWithAscii(data, fbxMagic))
            return "fbx";

        // GLB magic: 'glTF' (0x46546C67 little-endian).
        if (StartsWithAscii(data, "glTF"))
            return "gltf";

        var head = Encoding.UTF8.GetString(data, 0, Math.Min(data.Length, 4096));
        if (head.Contains("FBXHeaderExtension", StringComparison.Ordinal))
            return "fbx"; // ASCII FBX
        var trimmed = head.TrimStart();
        if (trimmed.StartsWith("HIERARCHY", StringComparison.OrdinalIgnoreCase))
            return "bvh";
        if (trimmed.StartsWith("{", StringComparison.Ordinal)
            && head.Contains("\"asset\"", StringComparison.Ordinal))
            return "gltf"; // plain-JSON glTF

        // RenderWare animation stream (.anm single clip / .an5 take container).
        if (RwAnmImporter.LooksLikeRenderwareAnim(data))
            return "rwanim";
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
    /// THE mapping cascade, shared by conversion, UI file listings, and custom-target
    /// detection: explicit override → authored mapping (from the file itself, e.g. a VRM's
    /// humanoid bone map — authoritative ground truth at confidence 1.0) → user preset (via
    /// <paramref name="userPresetLookup"/>, keyed by <see cref="Mapping.SkeletonSignature"/>)
    /// → shipped preset detection → best-effort auto-map. The report's
    /// <see cref="MappingReportInfo.NeedsUserDecision"/> is true only on the auto path below
    /// the preset detection threshold — conversion still proceeds with that map; callers
    /// decide whether to ask/reject.
    /// </summary>
    /// <param name="skeleton">Source (or candidate target) skeleton.</param>
    /// <param name="mappingOverride">Explicit mapping (manual table / already-resolved user
    /// preset); wins outright when non-null — a deliberate user decision beats even the
    /// file's own bone map.</param>
    /// <param name="userPresetLookup">Editor-side user-preset hook: receives the skeleton's
    /// signature, returns the stored mapping or null. The facade itself can do no file IO.</param>
    /// <param name="authoredMapping">A mapping authored INSIDE the source file
    /// (<see cref="SourceScene.AuthoredMapping"/>, <see cref="MappingSource.Authored"/>);
    /// consulted before user presets because the file itself is authoritative.</param>
    public static (MappingResult Map, MappingReportInfo Report) ResolveMapping(
        SkeletonModel skeleton, MappingResult? mappingOverride = null,
        Func<string, MappingResult?>? userPresetLookup = null,
        MappingResult? authoredMapping = null)
    {
        ArgumentNullException.ThrowIfNull(skeleton);

        if (mappingOverride is not null)
            return (mappingOverride, BuildReport(mappingOverride, needsUserDecision: false, skeleton));

        if (authoredMapping is not null)
            return (authoredMapping, BuildReport(authoredMapping, needsUserDecision: false, skeleton));

        if (userPresetLookup is not null
            && userPresetLookup(Mapping.SkeletonSignature.Compute(skeleton)) is { } userPreset)
            return (userPreset, BuildReport(userPreset, needsUserDecision: false, skeleton));

        if (ProfileDetector.Detect(skeleton) is { } detected)
            return (detected.Result, BuildReport(detected.Result, needsUserDecision: false, skeleton));

        var auto = AutoMapper.Map(skeleton);
        var needsUserDecision = auto.Confidence < ProfileDetector.DetectionThreshold;
        return (auto, BuildReport(auto, needsUserDecision, skeleton));
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
        if (!string.IsNullOrWhiteSpace(takeName) && takeName != "motion"
            && !takeName.Equals("mixamo.com", StringComparison.OrdinalIgnoreCase))
            return takeName;

        // BVH files (clip always named "motion"), Mixamo takes (always named
        // "mixamo.com") and unnamed takes use the file stem.
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

    /// <summary>
    /// Clip names become ModelDoc AnimFile node names and Source 2 sequence names, which
    /// must stay within <c>[A-Za-z0-9_]</c> — a take name like <c>mixamo.com</c> otherwise
    /// fails the vmdl compile with "Node 'mixamo.com' resolve failure". Runs of invalid
    /// characters collapse to a single underscore; case is preserved. Public so UIs that
    /// dry-run name-based detection (e.g. the locomotion-set scan over raw take names) see
    /// the SAME spelling conversion will produce — a take named <c>Walk N</c> becomes the
    /// clip <c>Walk_N</c>.
    /// </summary>
    public static string SanitizeClipName(string name)
    {
        var builder = new StringBuilder(name.Length);
        var lastWasUnderscore = false;
        foreach (var c in name)
        {
            if (c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_')
            {
                builder.Append(c);
                lastWasUnderscore = c == '_';
            }
            else if (!lastWasUnderscore)
            {
                builder.Append('_');
                lastWasUnderscore = true;
            }
        }
        var sanitized = builder.ToString().Trim('_');
        return sanitized.Length > 0 ? sanitized : "clip";
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

        /// <summary>ConstraintDriven bone indices (twist/helper) — excluded from DMX
        /// channels per design §3; null when the rig has none. Face bones
        /// (<see cref="SboxBoneClassifier.IsFaceBone"/>) are NOT in this set even though
        /// their class is ConstraintDriven: they keep rest-local channels, because no
        /// constraint re-drives them in a compiled sequence (channel-less face joints
        /// bake statically — eyes detach from the moving head in ModelDoc).</summary>
        public IReadOnlySet<int>? ConstraintDrivenBones { get; }

        private Dl.DlSolver? _dlSolver;

        /// <summary>The batch-shared deep-learning solver, parsing <paramref name="weights"/>
        /// once on first use (the batch runs requests sequentially — no locking needed).</summary>
        public IRetargetSolver GetDlSolver(byte[] weights)
            => _dlSolver ??= new Dl.DlSolver(weights);

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

            var constraintDriven = new HashSet<int>(rig.BonesOfClass(BoneClass.ConstraintDriven));
            // Face bones (eye_/ear_/face_) are ConstraintDriven by class but, unlike the
            // twist/helper bones, nothing re-drives them when a compiled sequence plays:
            // the model's AnimConstraintList never references them and the engine's eye
            // look-at / blinking only runs in game. They must KEEP their rest-local DMX
            // channels (the shipped fbx2dmx clips carry them too) or ModelDoc bakes the
            // channel-less joints statically and the eyes detach from the moving head
            // (see SboxBoneClassifier.IsFaceBone).
            constraintDriven.RemoveWhere(i => SboxBoneClassifier.IsFaceBone(rig.Skeleton[i].Name));
            ConstraintDrivenBones = constraintDriven.Count > 0 ? constraintDriven : null;

            try
            {
                Frame = CharacterFrame.Compute(rig.Skeleton, rig.ToMappingResult(), rig.Skeleton.RestWorld);
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
