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
        foreach (var (stem, label) in new[] { ("Walk", "Walk"), ("Run", "Run"), ("CrouchWalk", "Crouch walk") })
        foreach (var (direction, title) in new[] { ("N", "forward"), ("S", "backward"), ("E", "right"), ("W", "left"),
            ("NE", "forward-right"), ("NW", "forward-left"), ("SE", "backward-right"), ("SW", "backward-left") })
        {
            var sequences = stem == "Run" && direction == "N" ? new[] { "Run_N", "Run_N_m" } : new[] { stem + "_" + direction };
            slots.Add(new(stem.ToLowerInvariant() + "_" + direction.ToLowerInvariant(), label + " " + title, true, sequences,
                "Replaces only this direction at the normal speed tier. Other directions, fast/2× variants, blend thresholds and foot-sync settings stay unchanged. Use a matching looping clip; review foot sliding and transitions."));
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
        var doc = Parse(graph);
        var changed = 0;
        Visit(doc.Root, node =>
        {
            var name = node.GetString("m_sequenceName");
            if (name is null || (!slot.Sequences.Contains(name, StringComparer.Ordinal)
                && !name.StartsWith(slot.ReplacementPrefix, StringComparison.Ordinal))) return;
            node["m_sequenceName"] = new KvString(sequence);
            changed++;
        });
        if (changed == 0)
            throw new InvalidOperationException($"This graph has no compatible '{slot.Label}' sequence references. Its structure may have been customized; automatic replacement was not applied.");
        SetPreview((KvObject)doc.Root, modelPath);
        references = changed;
        return Kv3.Serialize(doc);
    }

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
