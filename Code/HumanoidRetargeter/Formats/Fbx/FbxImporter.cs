using System;
using System.Collections.Generic;
using System.Numerics;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;

namespace HumanoidRetargeter.Formats.Fbx;

using Vector3 = System.Numerics.Vector3; // s&box compat: shadow engine's global-namespace Vector3 (see Code/HumanoidRetargeter/Assembly.cs)

/// <summary>Options for <see cref="FbxImporter.Import"/>.</summary>
public sealed class FbxImportOptions
{
    /// <summary>Fixed resampling rate for all clips, frames per second.</summary>
    public float SampleFps { get; init; } = 30f;

    /// <summary>
    /// When the static rest pose is degenerate (Mixamo-style zeroed bind translations) and no
    /// usable BindPose node exists, sample frame 0 of the first clip as the rest pose.
    /// </summary>
    public bool RestFromFrame0WhenBindDegenerate { get; init; } = true;
}

/// <summary>
/// FBX → <see cref="SourceScene"/> importer: tokenize → semantic graph → skeleton model
/// selection → rest pose → clip resampling on a fixed fps grid.
/// </summary>
/// <remarks>
/// Unit policy: all translations are multiplied by GlobalSettings <c>UnitScaleFactor</c>
/// (source unit expressed in centimeters), producing centimeters. Axes are NOT converted;
/// the GlobalSettings axes are recorded on the <see cref="SourceScene"/>.
/// </remarks>
public static class FbxImporter
{
    /// <summary>Parses FBX bytes and builds the source scene.</summary>
    /// <exception cref="FormatException">Malformed FBX, or no skeleton-like nodes found.</exception>
    public static SourceScene Import(byte[] data, FbxImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        options ??= new FbxImportOptions();
        if (!(options.SampleFps > 0f) || !float.IsFinite(options.SampleFps))
            throw new ArgumentOutOfRangeException(nameof(options), "SampleFps must be positive.");

        var scene = FbxScene.Build(FbxTokenizer.Parse(data));
        float unitScale = (float)scene.UnitScaleFactor;

        var bones = SelectSkeletonModels(scene);
        if (bones.Count == 0)
            throw new FormatException("FBX contains no skeleton nodes (no LimbNode/Null models).");

        var ctx = new ImportContext(scene, bones, unitScale);

        // ---- rest pose -------------------------------------------------------------
        var restWorlds = EvaluateWorlds(ctx, null, 0);
        if (IsRestDegenerate(ctx))
        {
            if (TryBindPoseWorlds(ctx, out var bindWorlds))
                restWorlds = bindWorlds;
            else if (options.RestFromFrame0WhenBindDegenerate &&
                     FirstSampleableStack(ctx) is { } stack)
                restWorlds = EvaluateWorlds(ctx, stack, ClipStartTicks(ctx, stack));
        }

        var restLocals = WorldsToLocals(ctx, restWorlds);
        ApplyStaticTranslationChannels(ctx, restLocals);
        var skeleton = BuildSkeleton(ctx, restLocals);

        // ---- clips -----------------------------------------------------------------
        var clips = new List<Clip>();
        foreach (var stack in scene.Stacks)
        {
            var clip = SampleClip(ctx, skeleton, stack, options.SampleFps);
            if (clip is not null)
                clips.Add(clip);
        }

        return new SourceScene(
            skeleton, clips, unitScale,
            scene.UpAxis, scene.UpAxisSign,
            scene.FrontAxis, scene.FrontAxisSign,
            scene.CoordAxis, scene.CoordAxisSign,
            scene.OriginalUpAxis);
    }

    // =====================================================================================
    // skeleton model selection
    // =====================================================================================

    /// <summary>
    /// Picks the Models that form the skeleton: every LimbNode plus all of their Model
    /// ancestors (Null/Root containers included). Mesh leaves and other scene clutter are
    /// excluded. Fallback when the file has no LimbNodes at all: every non-Mesh model that
    /// is animated or has animated descendants; last resort, all non-Mesh models.
    /// Returned in parent-before-child order.
    /// </summary>
    private static List<FbxObject> SelectSkeletonModels(FbxScene scene)
    {
        var kept = new HashSet<long>();

        foreach (var model in scene.Models)
        {
            if (model.SubClass != "LimbNode" && model.SubClass != "Root")
                continue;
            // Keep the limb and walk every ancestor into the set.
            for (var m = model; m is not null && kept.Add(m.Id); m = m.ModelParent)
            {
            }
        }

        if (kept.Count == 0)
        {
            // No limbs: keep animated non-Mesh models and their ancestors.
            var animated = new HashSet<long>();
            foreach (var stack in scene.Stacks)
                foreach (var (modelId, _) in stack.Bindings.Keys)
                    animated.Add(modelId);

            foreach (var model in scene.Models)
            {
                if (model.SubClass == "Mesh" || !animated.Contains(model.Id))
                    continue;
                for (var m = model; m is not null && kept.Add(m.Id); m = m.ModelParent)
                {
                }
            }
        }

        if (kept.Count == 0)
        {
            foreach (var model in scene.Models)
                if (model.SubClass != "Mesh")
                    kept.Add(model.Id);
        }

        // Parent-before-child order via depth-first traversal from kept roots, following
        // document order among siblings.
        var result = new List<FbxObject>(kept.Count);
        var visited = new HashSet<long>();

        void Visit(FbxObject m)
        {
            if (!kept.Contains(m.Id) || !visited.Add(m.Id))
                return;
            result.Add(m);
            foreach (var child in m.ModelChildren)
                Visit(child);
        }

        foreach (var model in scene.Models)
            if (kept.Contains(model.Id) && NearestKeptAncestor(model, kept) is null)
                Visit(model);

        return result;
    }

    private static FbxObject? NearestKeptAncestor(FbxObject model, HashSet<long> kept)
    {
        for (var m = model.ModelParent; m is not null; m = m.ModelParent)
            if (kept.Contains(m.Id))
                return m;
        return null;
    }

    // =====================================================================================
    // evaluation
    // =====================================================================================

    /// <summary>Per-import precomputed state.</summary>
    private sealed class ImportContext
    {
        public FbxScene Scene { get; }
        public List<FbxObject> Bones { get; }
        public float UnitScale { get; }
        public Dictionary<long, int> BoneIndexById { get; } = new();
        public FbxTransform[] Transforms { get; }
        public int[] ParentIndex { get; }     // index into Bones, -1 for roots
        public string[] BoneNames { get; }    // deduplicated

        public ImportContext(FbxScene scene, List<FbxObject> bones, float unitScale)
        {
            Scene = scene;
            Bones = bones;
            UnitScale = unitScale;
            Transforms = new FbxTransform[bones.Count];
            ParentIndex = new int[bones.Count];
            BoneNames = new string[bones.Count];

            var keptIds = new HashSet<long>();
            foreach (var b in bones)
                keptIds.Add(b.Id);

            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < bones.Count; i++)
            {
                BoneIndexById[bones[i].Id] = i;
                Transforms[i] = FbxTransform.FromModel(scene, bones[i]);

                var parent = NearestKeptAncestor(bones[i], keptIds);
                ParentIndex[i] = parent is null ? -1 : BoneIndexById[parent.Id];

                string name = string.IsNullOrEmpty(bones[i].Name) ? $"bone_{bones[i].Id}" : bones[i].Name;
                if (!usedNames.Add(name))
                {
                    name = $"{name}#{bones[i].Id}";
                    usedNames.Add(name);
                }
                BoneNames[i] = name;
            }
        }
    }

    /// <summary>
    /// Evaluates world matrices for all skeleton bones — at rest (<paramref name="stack"/> null:
    /// static Lcl defaults + pivots/pre-rotations) or sampled from a stack at a KTIME tick.
    /// </summary>
    private static Matrix4x4[] EvaluateWorlds(ImportContext ctx, FbxAnimStack? stack, long ticks)
    {
        var worlds = new Matrix4x4[ctx.Bones.Count];
        for (int i = 0; i < ctx.Bones.Count; i++)
        {
            var xf = ctx.Transforms[i];
            Matrix4x4 local;
            if (stack is null)
            {
                local = xf.LocalMatrixDefault();
            }
            else
            {
                long id = ctx.Bones[i].Id;
                var t = SampleVector(stack, id, "Lcl Translation", ticks, xf.LclTranslation);
                var r = SampleVector(stack, id, "Lcl Rotation", ticks, xf.LclRotationDeg);
                var s = SampleVector(stack, id, "Lcl Scaling", ticks, xf.LclScaling);
                local = xf.LocalMatrix(t, r, s);
            }

            int parent = ctx.ParentIndex[i];
            worlds[i] = parent < 0 ? local : local * worlds[parent];
        }
        return worlds;
    }

    private static Vector3 SampleVector(
        FbxAnimStack stack, long modelId, string property, long ticks, Vector3 fallback)
    {
        if (!stack.Bindings.TryGetValue((modelId, property), out var cn))
            return fallback;
        return new Vector3(
            cn.Component('X', ticks, fallback.X),
            cn.Component('Y', ticks, fallback.Y),
            cn.Component('Z', ticks, fallback.Z));
    }

    /// <summary>Derives rigid (cm) parent-relative locals from world matrices, in bone order.</summary>
    private static XForm[] WorldsToLocals(ImportContext ctx, Matrix4x4[] worlds)
    {
        var rigid = new XForm[worlds.Length];
        for (int i = 0; i < worlds.Length; i++)
            rigid[i] = FbxTransform.ToRigid(worlds[i]);

        var locals = new XForm[worlds.Length];
        for (int i = 0; i < worlds.Length; i++)
        {
            int parent = ctx.ParentIndex[i];
            var local = parent < 0 ? rigid[i] : XForm.ToLocal(rigid[parent], rigid[i]);
            local.Pos *= ctx.UnitScale;
            locals[i] = local;
        }
        return locals;
    }

    // =====================================================================================
    // rest pose
    // =====================================================================================

    /// <summary>
    /// True when more than half of the non-root bones have near-zero static Lcl Translation —
    /// the Mixamo-style "zeroed bind" signature that makes the default rest unusable.
    /// </summary>
    private static bool IsRestDegenerate(ImportContext ctx)
    {
        int nonRoot = 0, zeroed = 0;
        for (int i = 0; i < ctx.Bones.Count; i++)
        {
            if (ctx.ParentIndex[i] < 0)
                continue;
            nonRoot++;
            if (ctx.Transforms[i].LclTranslation.LengthSquared() < 1e-6f)
                zeroed++;
        }
        return nonRoot > 0 && zeroed * 2 > nonRoot;
    }

    /// <summary>Bind-pose worlds when a Pose/BindPose node covers at least half the bones.</summary>
    private static bool TryBindPoseWorlds(ImportContext ctx, out Matrix4x4[] worlds)
    {
        worlds = Array.Empty<Matrix4x4>();
        if (ctx.Scene.BindPose.Count == 0)
            return false;

        int covered = 0;
        foreach (var b in ctx.Bones)
            if (ctx.Scene.BindPose.ContainsKey(b.Id))
                covered++;
        if (covered * 2 < ctx.Bones.Count)
            return false;

        // Missing entries fall back to the statically evaluated world.
        var evaluated = EvaluateWorlds(ctx, null, 0);
        worlds = new Matrix4x4[ctx.Bones.Count];
        for (int i = 0; i < ctx.Bones.Count; i++)
            worlds[i] = ctx.Scene.BindPose.TryGetValue(ctx.Bones[i].Id, out var m) ? m : evaluated[i];
        return true;
    }

    private static FbxAnimStack? FirstSampleableStack(ImportContext ctx)
    {
        foreach (var stack in ctx.Scene.Stacks)
            if (KeyRange(ctx, stack) is not null || stack.LocalStop > stack.LocalStart)
                return stack;
        return null;
    }

    /// <summary>
    /// Overrides rest local translation components with the animation's STATIC translation
    /// channel values: a translation curve that is constant across its keys (or single-keyed)
    /// is the rig geometry the animation actually plays, so the rest must use it — otherwise
    /// canonical chain directions are built from one geometry while the clip drives the bone
    /// with another.
    /// </summary>
    /// <remarks>
    /// <para>Evidence: UE Mannequin animation FBX files (dev/corpus/ue_mannequin,
    /// ThirdPersonWalk/Run) carry a BindPose whose foot→ball local offset disagrees with the
    /// clip's static ball translation channels by 7.938° on both feet (every other bone pair
    /// agrees to 0.000°). Building the rest from bind data alone produced a constant ~7.9°
    /// toe-pitch error in retargeted output (dev/verification/RESULTS.md, 2026-06 corpus run).</para>
    /// <para>Rules: only STATIC curves override — VARYING translation channels (e.g. hips
    /// trajectories) never touch the rest, so a clip starting mid-pose cannot corrupt rest hip
    /// height. FBX channels are per-axis (<c>d|X/Y/Z</c>); only the components that have a
    /// static curve are overridden, others keep the bind/Lcl-derived value. Rotations are
    /// untouched. A curve is static when its value range satisfies
    /// <c>max−min &lt; max(1e-3, 1e-5·max|value|)</c> in native file units (cm for these rigs).</para>
    /// </remarks>
    private static void ApplyStaticTranslationChannels(ImportContext ctx, XForm[] restLocals)
    {
        Span<float> statics = stackalloc float[3];
        Span<bool> hasStatic = stackalloc bool[3];

        for (int i = 0; i < ctx.Bones.Count; i++)
        {
            long id = ctx.Bones[i].Id;
            FbxAnimCurveNode? cn = null;
            foreach (var stack in ctx.Scene.Stacks)
                if (stack.Bindings.TryGetValue((id, "Lcl Translation"), out cn))
                    break;
            if (cn is null)
                continue;

            hasStatic.Clear();
            bool any = false;
            for (int axis = 0; axis < 3; axis++)
            {
                if (cn.Channels.TryGetValue("XYZ"[axis], out var curve) &&
                    TryGetStaticValue(curve, out statics[axis]))
                {
                    hasStatic[axis] = true;
                    any = true;
                }
            }
            if (!any)
                continue;

            // The Lcl translation enters the FBX local matrix purely additively after the
            // pivot/offset terms (see FbxTransform.LocalMatrix), so the parent-frame rest
            // position component is base + t. Rebuild the overridden components from the
            // static channel value on top of the pivot base; others keep the bind-derived pos.
            var xf = ctx.Transforms[i];
            var basePos = xf.LocalMatrix(Vector3.Zero, xf.LclRotationDeg, xf.LclScaling)
                .Translation;
            var pos = restLocals[i].Pos; // centimeters
            if (hasStatic[0]) pos.X = (basePos.X + statics[0]) * ctx.UnitScale;
            if (hasStatic[1]) pos.Y = (basePos.Y + statics[1]) * ctx.UnitScale;
            if (hasStatic[2]) pos.Z = (basePos.Z + statics[2]) * ctx.UnitScale;
            restLocals[i].Pos = pos;
        }
    }

    /// <summary>
    /// True when the curve is effectively constant: single-keyed, or its value range is below
    /// <c>max(1e-3, 1e-5·max|value|)</c> (native units). Returns the first key's value.
    /// </summary>
    private static bool TryGetStaticValue(FbxAnimCurve curve, out float value)
    {
        value = 0f;
        var values = curve.KeyValues;
        if (values.Length == 0)
            return false;

        float min = values[0], max = values[0], maxAbs = 0f;
        foreach (float v in values)
        {
            if (!float.IsFinite(v))
                return false;
            min = MathF.Min(min, v);
            max = MathF.Max(max, v);
            maxAbs = MathF.Max(maxAbs, MathF.Abs(v));
        }

        if (max - min >= MathF.Max(1e-3f, 1e-5f * maxAbs))
            return false;
        value = values[0];
        return true;
    }

    private static Skeleton.Skeleton BuildSkeleton(ImportContext ctx, XForm[] locals)
    {
        var defs = new List<BoneDefinition>(ctx.Bones.Count);
        for (int i = 0; i < ctx.Bones.Count; i++)
        {
            int parent = ctx.ParentIndex[i];
            defs.Add(new BoneDefinition(
                ctx.BoneNames[i],
                parent < 0 ? null : ctx.BoneNames[parent],
                locals[i]));
        }
        return Skeleton.Skeleton.Create(defs);
    }

    // =====================================================================================
    // clips
    // =====================================================================================

    /// <summary>
    /// Key-time range (KTIME ticks) over all curves bound to skeleton bones in the stack,
    /// or null when the stack has no keyed curves.
    /// </summary>
    private static (long Start, long Stop)? KeyRange(ImportContext ctx, FbxAnimStack stack)
    {
        long min = long.MaxValue, max = long.MinValue;
        foreach (var ((modelId, _), cn) in stack.Bindings)
        {
            if (!ctx.BoneIndexById.ContainsKey(modelId))
                continue;
            foreach (var curve in cn.Channels.Values)
            {
                if (curve.KeyTimes.Length == 0)
                    continue;
                min = Math.Min(min, curve.KeyTimes[0]);
                max = Math.Max(max, curve.KeyTimes[^1]);
            }
        }
        return min <= max ? (min, max) : null;
    }

    private static long ClipStartTicks(ImportContext ctx, FbxAnimStack stack)
        => KeyRange(ctx, stack) is { } range ? range.Start : stack.LocalStart;

    /// <summary>
    /// Samples one stack on the fps grid. The time range is the bound curves' key range
    /// (matching how Blender frames the action) with LocalStart/LocalStop as fallback.
    /// Returns null when the stack drives none of the skeleton bones.
    /// </summary>
    private static Clip? SampleClip(
        ImportContext ctx, Skeleton.Skeleton skeleton, FbxAnimStack stack, float fps)
    {
        long start, stop;
        if (KeyRange(ctx, stack) is { } range)
            (start, stop) = range;
        else if (stack.LocalStop > stack.LocalStart)
            (start, stop) = (stack.LocalStart, stack.LocalStop);
        else
            return null;

        double durationSeconds = (stop - start) / (double)FbxAnimCurve.TicksPerSecond;
        int frameCount = Math.Max(1, (int)Math.Round(durationSeconds * fps) + 1);

        // Skeleton bone order may differ from context bone order (topological sort) — map.
        var boneToSkeleton = new int[ctx.Bones.Count];
        for (int i = 0; i < ctx.Bones.Count; i++)
            boneToSkeleton[i] = skeleton.IndexOf(ctx.BoneNames[i]);

        var frames = new List<XForm[]>(frameCount);
        for (int f = 0; f < frameCount; f++)
        {
            long ticks = start + (long)Math.Round(f * (FbxAnimCurve.TicksPerSecond / (double)fps));
            var locals = WorldsToLocals(ctx, EvaluateWorlds(ctx, stack, ticks));

            var frame = new XForm[skeleton.Count];
            for (int i = 0; i < locals.Length; i++)
                frame[boneToSkeleton[i]] = locals[i];
            frames.Add(frame);
        }

        string name = string.IsNullOrEmpty(stack.Object.Name) ? "clip" : stack.Object.Name;
        return new Clip(name, fps, looping: false, frames);
    }
}
