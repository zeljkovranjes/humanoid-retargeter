#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HumanoidRetargeter.Core.Target;

/// <summary>
/// AnimGraph2 graph sources (<c>.ag</c>, plain JSON) and where a vmdl names one. Changes the graph's model and its
/// animation references only, never its nodes, parameters or variations.
/// </summary>
public static class AnimGraph2Document
{
    /// <summary>The vmdl <c>rootNode</c> key that assigns an AnimGraph2 graph; it sits beside AnimGraph1's
    /// <c>anim_graph_name</c>.</summary>
    public const string VmdlKey = "animation_graph_name";

    // Clip and pose nodes, and each variation override of them, name their animation with this key
    private const string AnimationName = "AnimationName";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Where a model's project-owned graph copy lives: <c>&lt;folder&gt;/graphs/&lt;model&gt;.ag</c>.</summary>
    public static string GraphPath(string outputFolder, string modelName)
        => (string.IsNullOrEmpty(outputFolder) ? "" : outputFolder.TrimEnd('/', '\\') + "/") + "graphs/" + modelName + ".ag";

    /// <summary>Points <paramref name="vmdl"/> at the AnimGraph2 graph <paramref name="graphPath"/>.</summary>
    public static string Attach(string vmdl, string graphPath)
    {
        var doc = Kv3.Parse(vmdl);
        ((KvObject)((KvObject)doc.Root)["rootNode"])[VmdlKey] = new KvString(graphPath);
        return Kv3.Serialize(doc);
    }

    /// <summary>Takes the AnimGraph2 graph off <paramref name="vmdl"/> (an empty name), leaving any AnimGraph1 graph
    /// to drive it. Returns <paramref name="vmdl"/> unchanged when it names none.</summary>
    public static string Detach(string vmdl)
        => string.IsNullOrEmpty(GraphName(vmdl)) ? vmdl : Attach(vmdl, "");

    /// <summary>The AnimGraph2 graph <paramref name="vmdl"/> names, or "" when it names none (an AnimGraph1 model).</summary>
    public static string GraphName(string vmdl)
        => ((KvObject)((KvObject)Kv3.Parse(vmdl).Root)["rootNode"]).GetString(VmdlKey) ?? "";

    /// <summary>The skeleton (<c>.skel</c>) the graph's default variation plays its clips on; a clip only plays on
    /// the skeleton it was compiled against.</summary>
    /// <exception cref="InvalidOperationException">The graph names no skeleton.</exception>
    public static string SkeletonOf(string graph)
        => Parse(graph)["Skeleton"]?.GetValue<string>() is { Length: > 0 } skeleton
            ? skeleton
            : throw new InvalidOperationException("The AnimGraph2 graph names no skeleton.");

    /// <summary>
    /// Copies all graph logic and clips for <paramref name="modelPath"/>, keeping the skeleton the clips were compiled
    /// against. Variations are dropped, with their overrides: each drives another model (the Human's female body), and
    /// the copy drives one.
    /// </summary>
    public static string CopyForModel(string graph, string modelPath)
    {
        var root = Parse(graph);
        root["Model"] = modelPath;
        root.Remove("variations");
        void Visit(JsonNode? node)
        {
            if (node is JsonArray array)
                foreach (var item in array) Visit(item);
            else if (node is JsonObject obj)
            {
                if (obj["Overrides"] is JsonArray { Count: > 0 })
                    obj["Overrides"] = new JsonArray();
                foreach (var (_, value) in obj) Visit(value);
            }
        }
        Visit(root);
        return root.ToJsonString(Indented);
    }

    /// <summary>
    /// Passes every animation the graph plays (each clip and pose node's <c>AnimationName</c>, including variation
    /// overrides) to <paramref name="replace"/>, and plays its result instead when that is not null.
    /// <paramref name="references"/> counts the references replaced.
    /// </summary>
    public static string RewriteAnimations(string graph, Func<string, string?> replace, out int references)
    {
        var root = Parse(graph);
        references = VisitAnimations(root, replace);
        return root.ToJsonString(Indented);
    }

    /// <summary>Every animation (<c>.anim</c>) the graph plays, once each, in the order the graph first names them.</summary>
    public static IReadOnlyList<string> Animations(string graph)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        VisitAnimations(Parse(graph), name =>
        {
            if (seen.Add(name)) names.Add(name);
            return null;
        });
        return names;
    }

    // Visits each AnimationName under node, replacing it with replace's result when that is not null; returns how many
    // it replaced
    private static int VisitAnimations(JsonNode? node, Func<string, string?> replace)
    {
        if (node is JsonArray array)
        {
            var count = 0;
            foreach (var item in array) count += VisitAnimations(item, replace);
            return count;
        }
        if (node is not JsonObject obj) return 0;
        var replaced = 0;
        if (obj[AnimationName] is JsonValue name && name.TryGetValue<string>(out var current)
            && replace(current) is { } replacement)
        {
            obj[AnimationName] = replacement;
            replaced++;
        }
        foreach (var (_, value) in obj) replaced += VisitAnimations(value, replace);
        return replaced;
    }

    /// <summary>The asset name of an animation reference, without folder or extension: <c>walk_forward</c> for
    /// <c>models/.../walk_forward.anim</c>.</summary>
    public static string AnimationStem(string animationName)
        => Path.GetFileNameWithoutExtension(animationName.Replace('\\', '/'));

    private static JsonObject Parse(string graph)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(graph); }
        catch (JsonException e) { throw new InvalidOperationException("Expected an editable s&box AnimGraph2 source (.ag): " + e.Message, e); }
        if (root is not JsonObject obj || obj["nodes"] is not JsonArray)
            throw new InvalidOperationException("Expected an editable s&box AnimGraph2 source (.ag).");
        return obj;
    }
}
