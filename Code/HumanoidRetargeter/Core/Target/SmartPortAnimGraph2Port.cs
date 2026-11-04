#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace HumanoidRetargeter.Core.Target;

/// <summary>
/// "Use the source's setup" for a source driven by AnimGraph2: its <c>.ag</c> graph, rebased onto the target as
/// <see cref="SmartPortSetup.Rebase"/> rebases an AnimGraph1 graph. AnimGraph2 compiles a graph only against a skeleton
/// (<c>.skel</c>) and clips (<c>.anim</c>) that have source files, so the target gets its own: a skeleton with the
/// source skeleton's bone masks, and a clip per animation the graph plays, retargeted like the source's sequences.
/// </summary>
public static class SmartPortAnimGraph2Port
{
    /// <summary>The additive type of a clip whose source file already holds deltas: relative to a pose of identities,
    /// which leaves them as they are.</summary>
    public const string AdditiveType = "RelativeToClip";

    // The node and parameter properties that name a bone (the AnimGraph2 editor's "animgraph.bone" properties)
    private static readonly HashSet<string> BoneProperties = new(StringComparer.Ordinal)
        { "Bone", "ReferenceBone", "EffectorBone", "LeftFootBone", "RightFootBone", "AlignmentBone", "PreviewStartBone" };

    private const string MasksKey = "m_maskDefinitions";
    private const string MaskSetsKey = "m_boneMaskSetDefinitions";
    private const string WeightListKey = "m_primaryWeightList";
    private const string BonesKey = "m_boneIDs";
    private const string WeightsKey = "m_weights";
    private const string DefaultChannelWeightKey = "m_defaultFloatChannelWeight";

    // Frames within which a sync event and an event start together
    private const double SyncTolerance = .01;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    // Compiled data marks some values with a type (resource:"path"), which source documents don't use
    private static readonly Regex ValueFlags = new(@"\b(resource|resource_name|soundevent|panorama|subclass):(?=[""{])", RegexOptions.Compiled);

    /// <summary>
    /// <paramref name="graph"/> (the source's <c>.ag</c>) for <paramref name="modelPath"/> on <paramref name="skeleton"/>:
    /// each animation it plays becomes its port in <paramref name="animations"/>, and each bone a node or parameter
    /// names becomes its target bone in <paramref name="boneNames"/> (source to target, as <see cref="SmartPortRig.BoneNames"/>).
    /// Variations are dropped (see <see cref="AnimGraph2Document.CopyForModel"/>). Throws when an animation has no port.
    /// </summary>
    public static string Rebase(string graph, string modelPath, string skeleton, IReadOnlyDictionary<string, string> animations,
        IReadOnlyDictionary<string, string> boneNames)
    {
        var copy = AnimGraph2Document.RewriteAnimations(AnimGraph2Document.CopyForModel(graph, modelPath),
            name => animations.TryGetValue(name, out var port) ? port
                : throw new InvalidOperationException("No ported clip for the source graph's animation " + name), out _);
        var root = (JsonObject)JsonNode.Parse(copy)!;
        root["Skeleton"] = skeleton;
        RenameBones(root, boneNames);
        return root.ToJsonString(Indented);
    }

    /// <summary>
    /// The <c>.skel</c> source of a skeleton with the bones of <paramref name="sourceFile"/> (a DMX in engine units) and the
    /// bone masks of <paramref name="compiledSkeleton"/> (the source skeleton's compiled data as KV3 text), each bone
    /// renamed by <paramref name="boneNames"/>. Mask entries the target lacks (morph channels, unmapped bones) are left
    /// out; a mask left with none keeps its name with a zero weight, so the graph's mask nodes still compile.
    /// </summary>
    public static string Skeleton(string sourceFile, string compiledSkeleton, IReadOnlyDictionary<string, string> boneNames,
        IReadOnlyList<string> targetBones)
    {
        if (targetBones.Count == 0) throw new InvalidOperationException("The target skeleton has no bones.");
        var bones = targetBones.ToHashSet(StringComparer.Ordinal);
        var document = Kv3.Parse(AnimGraph2Sources.Skeleton(sourceFile));
        var masks = new KvArray();
        var compiled = ((KvObject)Kv3.Parse(compiledSkeleton).Root).GetOrNull(MasksKey) as KvArray;
        foreach (var mask in compiled?.Items.OfType<KvObject>() ?? Enumerable.Empty<KvObject>())
        {
            if (mask.GetString("m_ID") is not { Length: > 0 } id) continue;
            var list = mask.GetOrNull(WeightListKey) as KvObject;
            var ids = (list?.GetOrNull(BonesKey) as KvArray)?.Items ?? new List<KvValue>();
            var weights = (list?.GetOrNull(WeightsKey) as KvArray)?.Items ?? new List<KvValue>();
            var names = new KvArray();
            var values = new KvArray();
            for (var i = 0; i < ids.Count; i++)
            {
                if (ids[i] is not KvString name) continue;
                var bone = boneNames.TryGetValue(name.Value, out var renamed) ? renamed : name.Value;
                if (!bones.Contains(bone) || names.Items.OfType<KvString>().Any(n => n.Value == bone)) continue;
                names.Items.Add(new KvString(bone));
                values.Items.Add(new KvDouble(i < weights.Count ? Number(weights[i]) : 1.0));
            }
            if (names.Items.Count == 0)
            {
                names.Items.Add(new KvString(targetBones[0]));
                values.Items.Add(new KvDouble(0));
            }
            masks.Items.Add(new KvObject
            {
                ["m_ID"] = new KvString(id),
                [WeightListKey] = new KvObject
                {
                    [BonesKey] = names,
                    [WeightsKey] = values,
                    [DefaultChannelWeightKey] = new KvDouble(list?.GetOrNull(DefaultChannelWeightKey) is { } d ? Number(d) : 1.0),
                },
            });
        }
        ((KvObject)document.Root)[MaskSetsKey] = masks;
        return Kv3.Serialize(document);
    }

    /// <summary>
    /// The <c>.anim</c> source of <paramref name="sourceFile"/> on <paramref name="skeleton"/>, in engine units. A clip whose
    /// source holds deltas names <paramref name="additiveBase"/>, a pose of identities, so it stays additive unchanged. The
    /// events and sync track of <paramref name="compiledClip"/> (the source clip's compiled data as KV3 text), when given,
    /// are kept: the source file has the same frames. <paramref name="rootMotion"/> names the axes the source clip moves
    /// along, which are extracted from the root bone (the source file carries its root motion in the root), or is null.
    /// </summary>
    public static string Clip(string sourceFile, string skeleton, string? additiveBase, string? compiledClip = null,
        (bool X, bool Y, bool Z, bool Yaw)? rootMotion = null)
    {
        var document = Kv3.Parse(AnimGraph2Sources.Clip(sourceFile, skeleton));
        var root = (KvObject)document.Root;
        if (additiveBase is not null)
        {
            root["m_additiveType"] = new KvString(AdditiveType);
            root["m_additiveBaseFilename"] = new KvString(additiveBase);
            root["m_nAdditiveBaseTake"] = new KvLong(0);
            root["m_nAdditiveBaseFrameIdx"] = new KvLong(0);
        }
        if (compiledClip is not null && EventTracks(compiledClip) is { Items.Count: > 0 } tracks) root["m_eventTracks"] = tracks;
        if (rootMotion is { } axes)
        {
            root["m_bExtractMotion"] = new KvBool(true);
            root["m_motionExtractionBone"] = new KvString("");
            root["m_bExtractMotionTX"] = new KvBool(axes.X);
            root["m_bExtractMotionTY"] = new KvBool(axes.Y);
            root["m_bExtractMotionTZ"] = new KvBool(axes.Z);
            root["m_bExtractMotionRZ"] = new KvBool(axes.Yaw);
        }
        return Kv3.Serialize(document);
    }

    // One event track per event class, in the order the compiled clip lists them. Compiled event times run from 0 to 1 over
    // the clip; source times are frames
    private static KvArray EventTracks(string compiledClip)
    {
        var data = (KvObject)Kv3.Parse(ValueFlags.Replace(compiledClip, "")).Root;
        var last = Math.Max((data.GetOrNull("m_nNumFrames") is { } frames ? Number(frames) : 1) - 1, 0);
        var tracks = new KvArray();
        foreach (var compiled in (data.GetOrNull("m_events") as KvArray)?.Items.OfType<KvObject>() ?? Enumerable.Empty<KvObject>())
        {
            var type = compiled.GetString("_class") ?? "";
            var track = tracks.Items.OfType<KvObject>().FirstOrDefault(t => t.GetString("m_eventClassName") == type);
            if (track is null)
            {
                track = new KvObject
                {
                    ["m_eventClassName"] = new KvString(type),
                    ["m_bIsSyncTrack"] = new KvBool(false),
                    ["m_bIsDisabled"] = new KvBool(false),
                    ["m_events"] = new KvArray(),
                };
                tracks.Items.Add(track);
            }
            var item = new KvObject();
            foreach (var key in compiled.Keys.Where(k => k != "_class"))
                item[key] = key is "m_startTime" or "m_duration" ? new KvDouble(Number(compiled[key]) * last) : compiled[key];
            ((KvArray)track["m_events"]).Items.Add(item);
        }
        // The sync track is the track whose events start where the compiled sync events do; they keep the sync IDs, which
        // compiled data has only as hashes (source documents may carry those instead of the names)
        var sync = ((data.GetOrNull("m_syncTrack") as KvObject)?.GetOrNull("m_syncEvents") as KvArray)?.Items.OfType<KvObject>().ToArray();
        if (sync is { Length: > 0 })
            foreach (var track in tracks.Items.OfType<KvObject>())
            {
                var events = ((KvArray)track["m_events"]).Items.OfType<KvObject>().OrderBy(e => Number(e["m_startTime"])).ToArray();
                if (events.Length != sync.Length || events.Where((e, i) => Math.Abs(Number(e["m_startTime"]) - Number(sync[i]["m_startTime"]) * last) > SyncTolerance).Any())
                    continue;
                track["m_bIsSyncTrack"] = new KvBool(true);
                for (var i = 0; i < events.Length; i++) events[i]["m_ID"] = sync[i]["m_ID"];
                break;
            }
        return tracks;
    }

    /// <summary>
    /// The IK goals of an AnimGraph2 graph, as <see cref="SmartPortRig"/> takes them: each bone a two-bone IK node
    /// reaches for (a constant bone target, possibly offset), mapped to the node's effector bone.
    /// </summary>
    public static IReadOnlyDictionary<string, string> IkTargets(string graph)
    {
        var nodes = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        Visit(JsonNode.Parse(graph));
        var goals = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in nodes.Values.Where(n => n["_class"]?.GetValue<string>() == "TwoBoneIKNode"))
        {
            if (node["DefaultVariationData"]?["EffectorBone"]?.GetValue<string>() is not { Length: > 0 } effector) continue;
            var target = Input(node, "EffectorTarget");
            while (target?["_class"]?.GetValue<string>() == "TargetOffsetNode") target = Input(target, "Target");
            if (target?["_class"]?.GetValue<string>() != "ConstBoneTargetNode" || target["Bone"]?.GetValue<string>() is not { Length: > 0 } goal)
                continue;
            if (goals.TryGetValue(goal, out var previous) && previous != effector)
                throw new InvalidOperationException("Conflicting IK effectors for goal: " + goal);
            goals[goal] = effector;
        }
        return goals;

        JsonObject? Input(JsonObject node, string input)
            => node[input]?["Identifier"] is JsonValue id && id.TryGetValue<string>(out var key) && nodes.TryGetValue(key, out var source) ? source : null;

        void Visit(JsonNode? node)
        {
            if (node is JsonArray array)
                foreach (var item in array) Visit(item);
            else if (node is JsonObject obj)
            {
                if (obj["_class"] is not null && obj["Identifier"] is JsonValue id && id.TryGetValue<string>(out var key)) nodes[key] = obj;
                foreach (var (_, value) in obj) Visit(value);
            }
        }
    }

    private static void RenameBones(JsonNode? node, IReadOnlyDictionary<string, string> boneNames)
    {
        if (node is JsonArray array)
            foreach (var item in array) RenameBones(item, boneNames);
        else if (node is JsonObject obj)
            foreach (var (key, value) in obj.ToArray())
                if (BoneProperties.Contains(key) && value is JsonValue v && v.TryGetValue<string>(out var bone)
                    && boneNames.TryGetValue(bone, out var renamed))
                    obj[key] = renamed;
                else
                    RenameBones(value, boneNames);
    }

    private static double Number(KvValue value) => value switch
    {
        KvDouble d => d.Value,
        KvLong l => l.Value,
        _ => throw new InvalidOperationException("Expected a number in the compiled skeleton's masks."),
    };
}
