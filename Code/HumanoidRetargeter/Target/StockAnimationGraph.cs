#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;

namespace HumanoidRetargeter.Target;

/// <summary>A supported stock graph slot, independent of the imported clip's format or rig.</summary>
public sealed record StockAnimationSlot(string Id, string Label, bool Looping, string[] Sequences, string Warning)
{
    public string ReplacementPrefix => "hr_replace_" + Id + "_";
}

/// <summary>The rewritten entry graph plus every subgraph it now references by a project-owned
/// copy, keyed by that copy's asset path.</summary>
public sealed record StockGraphReplacement(string Graph, IReadOnlyDictionary<string, string> Subgraphs, int References);

/// <summary>Changes sequence references, not graph connections, parameters, IK or additive layers.</summary>
public static class StockAnimationGraph
{
    public static IReadOnlyList<StockAnimationSlot> Slots { get; } = BuildSlots();

    private static StockAnimationSlot[] BuildSlots()
    {
        var slots = new List<StockAnimationSlot>
        {
            new("idle", "Idle (base pose)", true, new[] { "IdlePose_Default" },
                "Idle is a base pose: single-frame graph nodes use the clip's first frame. Animated idle layers and weapon poses stay unchanged."),
            new("jump", "Jump (standing)", false, new[] { "Jump_Standing" },
                "Replaces take-off only. Airborne and landing states stay unchanged; match the stock timing and check vertical motion."),
            new("jump_crouch", "Jump (crouching)", false, new[] { "Jump_Crouching" },
                "Replaces crouched take-off only; airborne and landing states stay unchanged.")
        };
        // Each direction is blended from several speed rings. The forward run, for one, holds
        // Run_N at ~180 and Sprint_N at ~300, so a controller running at 320 plays only the
        // sprint ring: replacing Run_N alone left a replaced run that never showed in game.
        // A slot therefore takes over its direction on every ring of its gait.
        var tiers = new Dictionary<string, string[]>
        {
            ["Walk"] = new[] { "Walk_{0}", "WalkFast_{0}", "Walk2X_{0}" },
            ["Run"] = new[] { "Run_{0}", "Run_{0}_m", "Run_{0}_f", "Run2X_{0}", "Sprint_{0}", "Sprint_{0}_m", "Sprint_{0}_f" },
            ["CrouchWalk"] = new[] { "CrouchWalk_{0}", "CrouchWalkLow_{0}" },
        };
        foreach (var (stem, label) in new[] { ("Walk", "Walk"), ("Run", "Run"), ("CrouchWalk", "Crouch walk") })
        foreach (var (direction, title) in new[] { ("N", "forward"), ("S", "backward"), ("E", "right"), ("W", "left"),
            ("NE", "forward-right"), ("NW", "forward-left"), ("SE", "backward-right"), ("SW", "backward-left") })
        {
            var sequences = tiers[stem].Select(t => string.Format(t, direction)).ToArray();
            slots.Add(new(stem.ToLowerInvariant() + "_" + direction.ToLowerInvariant(), label + " " + title, true, sequences,
                "Replaces this direction at every speed of the gait (for a run: run, fast run and sprint), so the clip plays at any movement speed. "
                + "Other directions, blend thresholds and foot-sync settings stay unchanged. Use a matching looping clip; review foot sliding and transitions."));
        }
        return slots.ToArray();
    }

    public static string GraphPath(string outputFolder, string modelName)
        => (string.IsNullOrEmpty(outputFolder) ? "" : outputFolder.TrimEnd('/', '\\') + "/") + "graphs/" + modelName + ".vanmgrph";

    public static string Attach(string vmdl, string graphPath)
    {
        var doc = Kv3.Parse(vmdl);
        ((KvObject)((KvObject)doc.Root)["rootNode"])["anim_graph_name"] = new KvString(graphPath);
        return Kv3.Serialize(doc);
    }

    public static string GraphName(string vmdl)
        => ((KvObject)((KvObject)Kv3.Parse(vmdl).Root)["rootNode"]).GetString("anim_graph_name") ?? "";

    /// <summary>Copies all graph logic and settings, changing only its editor preview model.</summary>
    public static string CopyForModel(string graph, string modelPath)
    {
        var doc = Parse(graph);
        SetPreview((KvObject)doc.Root, modelPath);
        return Kv3.Serialize(doc);
    }

    public static string Replace(string graph, StockAnimationSlot slot, string sequence, string modelPath, out int references)
    {
        var result = Replace(graph, slot, sequence, modelPath, _ => null, "");
        references = result.References;
        return result.Graph;
    }

    /// <summary>
    /// Points the slot's sequence references at <paramref name="sequence"/>, following subgraph
    /// nodes: the stock Citizen graph keeps all of its animation in subgraphs
    /// (citizen_core, citizen_locomotion, ...), so the entry graph alone holds no sequence. A
    /// subgraph that changes is copied to <paramref name="subgraphFolder"/> and its parent is
    /// pointed at the copy; shipped subgraphs are never modified. <paramref name="readSubgraph"/>
    /// returns a subgraph's source by asset path (the project copy first), or null.
    /// </summary>
    public static StockGraphReplacement Replace(string graph, StockAnimationSlot slot, string sequence, string modelPath,
        Func<string, string?> readSubgraph, string subgraphFolder)
    {
        var doc = Parse(graph);
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var visited = new Dictionary<string, (string Path, int Count)>(StringComparer.OrdinalIgnoreCase);
        var folder = subgraphFolder.Replace('\\', '/').TrimEnd('/');

        int Rewrite(KvValue root, HashSet<string> chain)
        {
            var changed = 0;
            Visit(root, node =>
            {
                var name = node.GetString("m_sequenceName");
                if (name is not null && (slot.Sequences.Contains(name, StringComparer.Ordinal)
                    || name.StartsWith(slot.ReplacementPrefix, StringComparison.Ordinal)))
                {
                    node["m_sequenceName"] = new KvString(sequence);
                    changed++;
                }

                var reference = node.GetString("m_subGraphFilename")?.Replace('\\', '/');
                if (string.IsNullOrEmpty(reference) || chain.Contains(reference))
                    return;
                if (!visited.TryGetValue(reference, out var copy))
                {
                    copy = (reference, 0);
                    if (readSubgraph(reference) is { } text)
                    {
                        var sub = Kv3.Parse(text);
                        chain.Add(reference);
                        var count = Rewrite(sub.Root, chain);
                        chain.Remove(reference);
                        if (count > 0)
                        {
                            var owned = folder.Length > 0 && reference.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)
                                ? reference
                                : (folder.Length > 0 ? folder + "/" : "") + reference[(reference.LastIndexOf('/') + 1)..];
                            outputs[owned] = Kv3.Serialize(sub);
                            copy = (owned, count);
                        }
                    }
                    visited[reference] = copy;
                }
                if (copy.Count > 0)
                {
                    node["m_subGraphFilename"] = new KvString(copy.Path);
                    changed += copy.Count;
                }
            });
            return changed;
        }

        var references = Rewrite(doc.Root, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        if (references == 0)
            throw new InvalidOperationException($"This graph has no compatible '{slot.Label}' sequence references. Its structure may have been customized; automatic replacement was not applied.");
        SetPreview((KvObject)doc.Root, modelPath);
        return new StockGraphReplacement(Kv3.Serialize(doc), outputs, references);
    }

    /// <summary>Where a model's project-owned subgraph copies live, beside its graph.</summary>
    public static string SubgraphFolder(string outputFolder, string modelName)
        => (string.IsNullOrEmpty(outputFolder) ? "" : outputFolder.TrimEnd('/', '\\') + "/") + "graphs/" + modelName + "_subgraphs";

    private static Kv3Document Parse(string text)
    {
        var doc = Kv3.Parse(text);
        if (doc.Root is not KvObject root || root.GetString("_class") != "CAnimationGraph")
            throw new InvalidOperationException("Expected an editable s&box animation graph source (.vanmgrph).");
        return doc;
    }

    private static void SetPreview(KvObject root, string modelPath)
    {
        var models = new KvArray();
        models.Items.Add(new KvString(modelPath));
        root["m_previewModels"] = models;
        root["m_boneMergeModels"] = new KvArray(); // do not dress the custom model in Citizen preview clothing
    }

    private static void Visit(KvValue value, Action<KvObject> action)
    {
        if (value is KvObject obj)
        {
            action(obj);
            foreach (var key in obj.Keys) Visit(obj[key], action);
        }
        else if (value is KvArray array)
            foreach (var item in array.Items) Visit(item, action);
    }
}
