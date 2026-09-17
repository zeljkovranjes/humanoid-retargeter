#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;

namespace HumanoidRetargeter.Target;

public sealed record SmartPortClip(string Name, bool Looping, bool Additive, bool Hidden);

/// <summary>Adds recovered clips without replacing the target's animation system.</summary>
public static class SmartPortExtension
{
    public const string Parameter = "hr_smartport_clip";

    public static string MergeModel(string targetText, string portedText, string graphPath,
        out IReadOnlyDictionary<string, string> names)
    {
        var doc = Kv3.Parse(targetText);
        var root = (KvObject)((KvObject)doc.Root)["rootNode"];
        var target = (KvArray)root["children"];
        var source = (KvArray)((KvObject)((KvObject)Kv3.Parse(portedText).Root)["rootNode"])["children"];
        KvObject? Category(KvArray list, string type) => list.Items.OfType<KvObject>().SingleOrDefault(n => n.GetString("_class") == type);
        var animations = Category(source, "AnimationList") ?? throw new InvalidOperationException("No recovered animation list.");
        var existing = Category(target, "AnimationList");
        var used = Objects(doc.Root).Select(n => n.GetString("name")).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        string Allocate(string name)
        {
            var stem = "hr_port_" + name;
            var result = stem;
            for (var suffix = 2; !used.Add(result); suffix++) result = stem + "_" + suffix;
            return result;
        }
        // Recovery flattens sequence processing into AnimFile channels; keep their events.
        foreach (var clip in Objects(animations).Where(n => n.GetString("_class") == "AnimFile"))
        {
            var name = clip.GetString("name") ?? throw new InvalidOperationException("Unnamed recovered animation.");
            map.Add(name, Allocate(name));
            clip["name"] = new KvString(map[name]);
        }
        if (map.Count == 0) throw new InvalidOperationException("No recovered clips to add.");
        foreach (var node in Objects(animations))
            if (node.GetString("anim_name") is string name && map.TryGetValue(name, out var renamed))
                node["anim_name"] = new KvString(renamed);
        // Weight-list names have their own references and must not collide with stock masks.
        if (Category(source, "WeightListList") is KvObject masks)
        {
            var maskNames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var mask in Objects(masks).Where(n => n.GetString("_class") == "WeightList"))
            {
                var name = mask.GetString("name")!;
                maskNames.Add(name, Allocate(name));
                mask["name"] = new KvString(maskNames[name]);
            }
            foreach (var node in Objects(animations))
                if (node.GetString("weight_list_name") is string name && maskNames.TryGetValue(name, out var renamed))
                    node["weight_list_name"] = new KvString(renamed);
            var destination = Category(target, "WeightListList");
            if (destination is null) target.Items.Add(masks);
            else ((KvArray)destination["children"]).Items.AddRange(((KvArray)masks["children"]).Items);
        }
        if (existing is null) target.Items.Add(animations);
        else ((KvArray)existing["children"]).Items.AddRange(((KvArray)animations["children"]).Items);
        // The ported skeleton includes any extra source helper bones; original target binds stay intact.
        if (Category(source, "Skeleton") is KvObject skeleton)
        {
            target.Items.RemoveAll(n => n is KvObject o && o.GetString("_class") == "Skeleton");
            target.Items.Add(skeleton);
        }
        root["anim_graph_name"] = new KvString(graphPath);
        names = map;
        return Kv3.Serialize(doc);
    }

    public static string ExtendGraph(string graph, string modelPath, IReadOnlyList<SmartPortClip> clips, out string parameter)
    {
        if (clips.Count == 0) throw new InvalidOperationException("No visible clips to connect to the graph.");
        var doc = Kv3.Parse(StockAnimationGraph.CopyForModel(graph, modelPath));
        var root = (KvObject)doc.Root;
        if (root.GetOrNull("m_nodeManager") is not KvObject manager || manager.GetOrNull("m_nodes") is not KvArray nodes)
            throw new InvalidOperationException("This target graph has no editable node list.");
        var list = root.GetOrNull("m_pParameterList") as KvObject ?? new KvObject { ["_class"] = new KvString("CAnimParameterList") };
        var parameters = list.GetOrNull("m_Parameters") as KvArray ?? new KvArray();
        list["m_Parameters"] = parameters;
        root["m_pParameterList"] = list;
        var outputs = nodes.Items.OfType<KvObject>().Select(n => n.GetOrNull("value")).OfType<KvObject>()
            .Where(n => n.GetString("_class") == "CRootAnimNode").ToArray();
        if (outputs.Length != 1 || outputs[0].GetOrNull("m_inputConnection") is not KvObject original)
            throw new InvalidOperationException("This target graph must have one connected output.");
        if (original.GetOrNull("m_nodeID") is not KvObject input || input.GetOrNull("m_id") is not KvLong inputId
            || inputId.Value == uint.MaxValue || !nodes.Items.OfType<KvObject>().Any(n =>
                n.GetOrNull("key") is KvObject key && key.GetOrNull("m_id") is KvLong id && id.Value == inputId.Value))
            throw new InvalidOperationException("The target graph output is disconnected.");
        var ids = Objects(root).Select(n => n.GetOrNull("m_id")).OfType<KvLong>().Select(n => n.Value).ToHashSet();
        long next = 1;
        long Allocate() { while (!ids.Add(next)) next++; return next++; }
        var usedNames = parameters.Items.OfType<KvObject>().Select(n => n.GetString("m_name")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        parameter = Parameter;
        for (var suffix = 2; usedNames.Contains(parameter); suffix++) parameter = Parameter + "_" + suffix;
        var parameterId = Allocate();
        var options = Array(new KvString("Existing animations"));
        var children = Array(original);
        var tags = Array(Id(uint.MaxValue));
        var positions = nodes.Items.OfType<KvObject>().Select(n => n.GetOrNull("value")).OfType<KvObject>()
            .Select(n => n.GetOrNull("m_vecPosition")).OfType<KvArray>().Where(p => p.Items.Count >= 2).ToArray();
        static double Coordinate(KvValue v) => v is KvDouble d ? d.Value : v is KvLong l ? l.Value : 0;
        var startX = positions.Select(p => Coordinate(p.Items[0])).DefaultIfEmpty(0).Max() + 500;
        var startY = positions.Select(p => Coordinate(p.Items[1])).DefaultIfEmpty(0).Min();
        var added = 0;
        long Add(KvObject node)
        {
            var id = Allocate();
            node["m_nNodeID"] = Id(id);
            // Leave the user's layout intact; new nodes get their own area instead of stacking at (0, 0).
            node["m_vecPosition"] = Array(new KvDouble(startX + added % 4 * 500), new KvDouble(startY + added / 4 * 300));
            added++;
            nodes.Items.Add(new KvObject { ["key"] = Id(id), ["value"] = node });
            return id;
        }
        foreach (var clip in clips)
        {
            var id = Add(new KvObject
            {
                ["_class"] = new KvString("CSequenceAnimNode"), ["m_sName"] = new KvString(clip.Name),
                ["m_sequenceName"] = new KvString(clip.Name), ["m_playbackSpeed"] = new KvDouble(1),
                ["m_bLoop"] = new KvBool(clip.Looping), ["m_tagSpans"] = new KvArray()
            });
            if (clip.Additive)
                id = Add(new KvObject
                {
                    ["_class"] = new KvString("CAddAnimNode"), ["m_sName"] = new KvString(clip.Name + " (additive)"),
                    ["m_baseInput"] = original, ["m_additiveInput"] = Connection(id),
                    ["m_timingBehavior"] = new KvString("UseChild1"), ["m_flTimingBlend"] = new KvDouble(.5),
                    ["m_footMotionTiming"] = new KvString("Child1"), ["m_bResetBase"] = new KvBool(false),
                    ["m_bResetAdditive"] = new KvBool(true), ["m_bApplyChannelsSeparately"] = new KvBool(true)
                });
            children.Items.Add(Connection(id));
            tags.Items.Add(Id(uint.MaxValue));
            options.Items.Add(new KvString(clip.Name));
        }
        parameters.Items.Add(new KvObject
        {
            ["_class"] = new KvString("CEnumAnimParameter"), ["m_name"] = new KvString(parameter),
            ["m_id"] = Id(parameterId), ["m_defaultValue"] = new KvLong(0), ["m_enumOptions"] = options,
            ["m_bAutoReset"] = new KvBool(false), ["m_bUseMostRecentValue"] = new KvBool(true),
            ["m_previewButton"] = new KvString("ANIMPARAM_BUTTON_NONE")
        });
        var selector = Add(new KvObject
        {
            ["_class"] = new KvString("CSelectorAnimNode"), ["m_sName"] = new KvString("Smart Port clips"),
            ["m_children"] = children, ["m_tags"] = tags, ["m_selectionSource"] = new KvString("SelectionSource_Enum"),
            ["m_enumParamID"] = Id(parameterId), ["m_boolParamID"] = Id(uint.MaxValue), ["m_intParamID"] = Id(uint.MaxValue),
            ["m_intParamMinValue"] = new KvLong(0), ["m_intParamMaxValue"] = new KvLong(clips.Count),
            ["m_blendDuration"] = new KvDouble(.2), ["m_bResetOnChange"] = new KvBool(true),
            ["m_bSyncCyclesOnChange"] = new KvBool(false), ["m_tagBehavior"] = new KvString("SelectorTagBehavior_OffWhenFinished"),
            ["m_intParamNames"] = new KvArray(),
            ["m_blendCurve"] = new KvObject { ["m_vControlPoint1"] = Array(new KvDouble(.25), new KvDouble(0)),
                ["m_vControlPoint2"] = Array(new KvDouble(.75), new KvDouble(1)) }
        });
        outputs[0]["m_inputConnection"] = Connection(selector);
        return Kv3.Serialize(doc);
    }

    static KvObject Id(long id) => new() { ["m_id"] = new KvLong(id) };
    static KvObject Connection(long id) => new() { ["m_nodeID"] = Id(id), ["m_outputID"] = Id(uint.MaxValue) };
    static KvArray Array(params KvValue[] values) { var array = new KvArray(); array.Items.AddRange(values); return array; }
    static IEnumerable<KvObject> Objects(KvValue value)
    {
        if (value is KvObject obj)
        {
            yield return obj;
            foreach (var key in obj.Keys)
                foreach (var child in Objects(obj[key])) yield return child;
        }
        else if (value is KvArray array)
            foreach (var item in array.Items)
                foreach (var child in Objects(item)) yield return child;
    }
}
