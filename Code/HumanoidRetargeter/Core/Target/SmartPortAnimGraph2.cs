#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HumanoidRetargeter.Core.Target;

/// <summary>
/// <see cref="SmartPortExtension.ExtendGraph"/> for a target driven by AnimGraph2: adds the ported clips to its
/// <c>.ag</c> graph behind the same selection, an ID parameter whose options keep the AnimGraph1 enum's order (the
/// original graph first, then one per clip), so game code picks a clip by the same index or option name. AnimGraph2
/// selectors choose once on entry, so the choice is a state machine with a global transition into each state; a
/// state machine holds at most <see cref="MaxStates"/> states, so longer clip lists nest. The original graph moves
/// unchanged into the first state. Additive clips play in a second state machine, layered additively over the first,
/// which then keeps the original graph.
/// </summary>
public static class SmartPortAnimGraph2
{
    /// <summary>The most states one AnimGraph2 state machine compiles with.</summary>
    public const int MaxStates = 64;

    // AnimGraph1's selector blends over the same time (SmartPortExtension's m_blendDuration)
    private const double BlendSeconds = 0.2;
    private const string AdditiveNone = "No additive clip";
    private const string ParameterGroup = "Smart Port";
    private const string PoseOutput = "Result";
    private const string ValueOutput = "Value";
    private const string LayerOutput = "Layer";
    private const int ColumnWidth = 320;
    private const int RowHeight = 180;
    // Centimetres to engine units: the scale the stock Citizen sources are imported at
    private const double CentimetreScale = 0.3937;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// <paramref name="graph"/> (<c>.ag</c> JSON) for <paramref name="modelPath"/>, with <paramref name="clips"/> added.
    /// <paramref name="animations"/> maps each clip name to its <c>.anim</c>. <paramref name="parameter"/> is the
    /// selection parameter's name: <see cref="SmartPortExtension.Parameter"/>, suffixed when the graph already uses it.
    /// Throws when there are no clips, a clip has no animation, or the graph has no single connected output.
    /// </summary>
    public static string ExtendGraph(string graph, string modelPath, IReadOnlyList<SmartPortClip> clips,
        IReadOnlyDictionary<string, string> animations, out string parameter)
    {
        if (clips.Count == 0) throw new InvalidOperationException("No visible clips to connect to the graph.");
        var root = (JsonObject)JsonNode.Parse(AnimGraph2Document.CopyForModel(graph, modelPath))!;
        var nodes = (JsonArray)root["nodes"]!;
        var outputs = nodes.OfType<JsonObject>().Where(n => Class(n) == "PoseResultNode").ToArray();
        if (outputs.Length != 1 || outputs[0]["Pose"]?["Identifier"]?.GetValue<string>() is not { Length: > 0 } input)
            throw new InvalidOperationException("This target graph must have one connected output.");
        if (!nodes.OfType<JsonObject>().Any(n => n["Identifier"]?.GetValue<string>() == input))
            throw new InvalidOperationException("The target graph output is disconnected.");

        var builder = new Builder(modelPath, Identifiers(root));
        var parameters = root["parameters"] as JsonArray ?? new JsonArray();
        root["parameters"] = parameters;
        var used = new[] { parameters, root["virtualParameters"] as JsonArray }.Where(a => a is not null)
            .SelectMany(a => a!.OfType<JsonObject>()).Select(p => p["Name"]?.GetValue<string>())
            .OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        parameter = SmartPortExtension.Parameter;
        for (var suffix = 2; used.Contains(parameter); suffix++) parameter = SmartPortExtension.Parameter + "_" + suffix;
        var options = clips.Select(c => c.Name).Prepend(SmartPortExtension.OriginalOption).ToArray();
        builder.Parameter = (builder.NewId(), parameter);
        parameters.Add(new JsonObject
        {
            ["Identifier"] = builder.Parameter.Identifier,
            ["Type"] = "ID",
            ["Name"] = parameter,
            ["Group"] = ParameterGroup,
            ["RuntimeDefaultID"] = SmartPortExtension.OriginalOption,
            ["DefaultID"] = SmartPortExtension.OriginalOption,
            ["ExpectedValues"] = Strings(options),
        });

        foreach (var clip in clips)
            if (!animations.ContainsKey(clip.Name))
                throw new InvalidOperationException($"Clip '{clip.Name}' has no AnimGraph2 animation (.anim).");
        var additive = clips.Where(c => c.Additive).Select(c => c.Name).ToArray();

        // The original graph, root output and all, becomes the first state's graph
        var original = new JsonArray();
        while (nodes.Count > 0)
        {
            var node = nodes[0];
            nodes.RemoveAt(0);
            original.Add(node);
        }
        var full = clips.Where(c => !c.Additive).Select(c => builder.ClipChoice(c, animations[c.Name]))
            .Prepend(new Choice(SmartPortExtension.OriginalOption, additive.Prepend(SmartPortExtension.OriginalOption).ToArray(), () => original))
            .ToList();
        var pose = builder.Machine(full, "Smart Port clips", nodes);
        if (additive.Length > 0)
        {
            var others = options.Except(additive).ToArray();
            // An "Off" state adds nothing while no additive clip is selected
            var layered = clips.Where(c => c.Additive).Select(c => builder.ClipChoice(c, animations[c.Name]))
                .Prepend(new Choice(AdditiveNone, others, null))
                .ToList();
            var layer = Node(builder, "StateMachineLayerNode", 1, 1,
                ("StateMachine", builder.Machine(layered, "Smart Port additive clips", nodes)),
                ("BlendMode", "Additive"), ("IsSynchronized", false), ("IgnoreEvents", false));
            var blend = Node(builder, "LayerBlendNode", 2, 0,
                ("Base", pose), ("Layers", new JsonArray(Ref(builder.Add(nodes, layer), LayerOutput))), ("OnlySampleBaseRootMotion", true));
            pose = builder.Add(nodes, blend, PoseOutput);
        }
        nodes.Insert(0, Node(builder, "PoseResultNode", 3, 0, ("Pose", pose)));
        return root.ToJsonString(Indented);
    }

    /// <summary>
    /// The import scale for clips recovered in engine units on the skeleton whose <c>.skel</c> source is
    /// <paramref name="skeleton"/>: AnimGraph2 scales a clip by its skeleton's global scale (0.3937 for centimetre
    /// art), which recovered clips already carry. Throws when that scale isn't a positive number.
    /// </summary>
    public static double ClipImportScale(string skeleton) => 1 / GlobalScale(skeleton);

    /// <summary>
    /// The import scale for a clip whose source is in centimetres, the stock Citizen units (a retargeted stock
    /// replacement), on the skeleton whose <c>.skel</c> source is <paramref name="skeleton"/>: 1 on the shipped
    /// skeletons, which scale centimetres themselves, and 0.3937 on one ported in engine units. Throws when the
    /// skeleton's global scale isn't a positive number.
    /// </summary>
    public static double CentimetreClipScale(string skeleton) => CentimetreScale / GlobalScale(skeleton);

    private static double GlobalScale(string skeleton)
    {
        var scale = ((KvObject)Kv3.Parse(skeleton).Root).GetOrNull("m_flGlobalScale") switch
        {
            null => 1.0,
            KvDouble d => d.Value,
            KvLong l => l.Value,
            _ => double.NaN,
        };
        if (!(scale > 0) || double.IsInfinity(scale))
            throw new InvalidOperationException($"The AnimGraph2 skeleton's global scale ({scale}) isn't a positive number.");
        return scale;
    }

    /// <summary>The source file each <c>AnimFile</c> in <paramref name="vmdl"/> plays, by animation name.</summary>
    public static IReadOnlyDictionary<string, string> AnimationFiles(string vmdl)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        Visit(Kv3.Parse(vmdl).Root);
        return files;

        void Visit(KvValue value)
        {
            if (value is KvObject node)
            {
                if (node.GetString("_class") == "AnimFile" && node.GetString("name") is { } name
                    && node.GetString("source_filename") is { Length: > 0 } source)
                    files[name] = source;
                foreach (var key in node.Keys) Visit(node[key]);
            }
            else if (value is KvArray array)
                foreach (var item in array.Items) Visit(item);
        }
    }

    // A state to select: its name, the parameter options that select it and the graph it plays (with its result
    // node), or null for a state that plays nothing
    private sealed record Choice(string Name, string[] Ids, Func<JsonArray>? Graph);

    private sealed class Builder(string seed, HashSet<string> identifiers)
    {
        private int _next;

        public (string Identifier, string Name) Parameter;

        // Stable identifiers: the same port of the same model always writes the same graph
        public string NewId()
        {
            string id;
            do id = StableGuid(seed + "/" + _next++);
            while (!identifiers.Add(id));
            return id;
        }

        public JsonObject Add(JsonArray nodes, JsonObject node, string? output = null)
        {
            nodes.Add(node);
            return Ref(node, output ?? PoseOutput);
        }

        // A state graph: its pose (built into nodes) feeding the graph's result node
        public JsonArray Pose(Func<JsonArray, JsonObject> build)
        {
            var nodes = new JsonArray();
            var pose = build(nodes);
            nodes.Insert(0, Node(this, "PoseResultNode", 2, 0, ("Pose", pose)));
            return nodes;
        }

        public Choice ClipChoice(SmartPortClip clip, string animation)
            => new(clip.Name, new[] { clip.Name }, () => Pose(nodes => Add(nodes, Node(this, "AnimationClipNode", 0, 0,
                ("Loop", clip.Looping), ("SampleRootMotion", false),
                ("DefaultVariationData", new JsonObject { ["AnimationName"] = animation, ["SpeedMultiplier"] = 1, ["StartSyncEventOffset"] = 0 }),
                ("Overrides", new JsonArray())))));

        /// <summary>A state machine (added to <paramref name="nodes"/>) playing the choice the parameter selects.</summary>
        public JsonObject Machine(IReadOnlyList<Choice> choices, string name, JsonArray nodes)
        {
            // The first state (the original graph, or no additive clip) stays at the top; the rest nest in groups
            if (choices.Count > MaxStates)
            {
                var size = (choices.Count - 1 + MaxStates - 2) / (MaxStates - 1);
                choices = choices.Skip(1).Select((c, i) => (c, i)).GroupBy(p => p.i / size, p => p.c).Select(g =>
                {
                    var group = g.ToList();
                    return new Choice($"{name} {g.Key + 1}", group.SelectMany(c => c.Ids).ToArray(),
                        () => Pose(inner => Machine(group, $"{name} {g.Key + 1}", inner)));
                }).Prepend(choices[0]).ToList();
            }
            var global = new JsonArray();
            var entry = new JsonArray();
            var globalId = Add(global, ParameterNode(), ValueOutput);
            var entryId = Add(entry, ParameterNode(), ValueOutput);
            var states = new JsonArray();
            var conditions = new JsonArray();
            for (var i = 0; i < choices.Count; i++)
            {
                var choice = choices[i];
                states.Add(new JsonObject
                {
                    ["Name"] = choice.Name,
                    ["Type"] = choice.Graph is null ? "Off" : "BlendTree",
                    ["Events"] = new JsonArray(),
                    ["TimedEvents"] = new JsonArray(),
                    ["TransitionOrder"] = new JsonArray(),
                    ["Position"] = Position(i % 8, i / 8),
                    ["Graph"] = new JsonObject { ["Version"] = 1, ["nodes"] = choice.Graph?.Invoke() ?? new JsonArray(Node(this, "PoseResultNode", 2, 0)) },
                });
                global.Add(Node(this, "GlobalTransitionNode", 2, i,
                    ("StateName", choice.Name), ("Condition", Add(global, Matches(globalId, choice.Ids, i))),
                    ("TransitionName", "To " + choice.Name), ("Duration", BlendSeconds), ("TimeMatch", "None"),
                    ("BlendWeightEasing", "Linear"), ("RootMotionBlend", "Blend"), ("CanBeForced", true)));
                conditions.Add(Add(entry, Matches(entryId, choice.Ids, i)));
            }
            entry.Add(Node(this, "EntryStateOverrideConditionsNode", 2, 0,
                ("States", Strings(choices.Select(c => c.Name))), ("Conditions", conditions)));
            return Add(nodes, Node(this, "StateMachineNode", 0, 0,
                ("States", states), ("Conduits", new JsonArray()),
                ("GlobalGraph", new JsonObject { ["Version"] = 1, ["nodes"] = global, ["Kind"] = "GlobalTransitions" }),
                ("EntryGraph", new JsonObject { ["Version"] = 1, ["nodes"] = entry, ["Kind"] = "EntryOverrides" }),
                ("DefaultState", choices[0].Name)));
        }

        private JsonObject ParameterNode()
            => Node(this, "IDParameterNode", 0, 0, ("ParameterIdentifier", Parameter.Identifier), ("ParameterName", Parameter.Name));

        private JsonObject Matches(JsonObject id, IEnumerable<string> options, int row)
            => Node(this, "IDComparisonNode", 1, row, ("Id", id.DeepClone()), ("Comparison", "Matches"), ("Ids", Strings(options)));
    }

    private static JsonObject Node(Builder builder, string type, int column, int row, params (string Key, JsonNode? Value)[] properties)
    {
        var node = new JsonObject { ["_class"] = type };
        foreach (var (key, value) in properties) node[key] = value;
        node["Identifier"] = builder.NewId();
        node["Position"] = Position(column, row);
        node["HandleOffsets"] = new JsonObject();
        return node;
    }

    private static JsonObject Ref(JsonObject node, string output)
        => new() { ["Identifier"] = node["Identifier"]!.GetValue<string>(), ["Output"] = output };

    private static string Position(int column, int row)
        => string.Create(CultureInfo.InvariantCulture, $"{column * ColumnWidth},{row * RowHeight}");

    private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

    private static string? Class(JsonObject node) => node["_class"]?.GetValue<string>();

    // Every identifier already in the document: new ones must be unique across all nested graphs
    private static HashSet<string> Identifiers(JsonNode root)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(JsonNode? node)
        {
            if (node is JsonArray array)
                foreach (var item in array) Visit(item);
            else if (node is JsonObject obj)
                foreach (var (key, value) in obj)
                    if (key == "Identifier" && value is JsonValue v && v.TryGetValue<string>(out var id)) ids.Add(id);
                    else Visit(value);
        }
        Visit(root);
        return ids;
    }

    // FNV-1a over the text, twice with different bases: a GUID that is the same on every run and machine
    private static string StableGuid(string text)
    {
        var bytes = new byte[16];
        var data = Encoding.UTF8.GetBytes(text);
        for (var half = 0; half < 2; half++)
        {
            var hash = half == 0 ? 14695981039346656037UL : 1099511628211UL * 31;
            foreach (var b in data) hash = (hash ^ b) * 1099511628211UL;
            BitConverter.GetBytes(hash).CopyTo(bytes, half * 8);
        }
        return new Guid(bytes).ToString();
    }
}
