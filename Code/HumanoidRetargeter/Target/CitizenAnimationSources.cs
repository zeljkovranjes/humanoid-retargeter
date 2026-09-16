#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;

namespace HumanoidRetargeter.Target;

/// <summary>Expands animation-list prefabs without flattening away any sequence processing or events.</summary>
public sealed class CitizenAnimationSources
{
    public sealed class Source
    {
        public string Name { get; init; } = "";
        public string Filename { get; init; } = "";
        public long Take { get; init; }
        public List<KvObject> Users { get; } = new();

        public void SetConvertedFile(string filename, float nativeFps)
        {
            foreach (var user in Users)
            {
                user["source_filename"] = new KvString(filename);
                user["take"] = new KvLong(0);
                // Frame ranges, holds, events and speed overrides still index the native grid.
                if (Number(user.GetOrNull("framerate"), -1) <= 0)
                    user["framerate"] = new KvDouble(nativeFps);
            }
        }
    }

    private readonly Kv3Document document;
    public IReadOnlyList<Source> Sources { get; }
    public string ModelText => Kv3.Serialize(document);

    public CitizenAnimationSources(string vmdl, Func<string, string> readPrefab)
    {
        document = Kv3.Parse(vmdl);
        var animationList = Children(document).Items.OfType<KvObject>().Single(n => n.GetString("_class") == "AnimationList");
        Expand(animationList, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var sources = new Dictionary<(string, long), Source>();
        Visit(animationList);
        Sources = sources.Values.ToArray();

        void Expand(KvObject node, HashSet<string> active)
        {
            if (node.GetOrNull("children") is not KvArray children) return;
            for (var i = 0; i < children.Items.Count; i++)
            {
                if (children.Items[i] is not KvObject child) continue;
                if (child.GetString("_class") == "Prefab")
                {
                    var path = child.GetString("target_file") ?? throw new FormatException("Animation prefab has no filename.");
                    if (!active.Add(path)) throw new FormatException("Cyclic animation prefab: " + path);
                    var roots = Children(Kv3.Parse(readPrefab(path)));
                    // Processing prefabs (ExtractMotion, footsteps, etc.) remain intact.
                    if (roots.Items.OfType<KvObject>().Any(ContainsAnimations))
                    {
                        var entries = new KvArray();
                        foreach (var root in roots.Items.OfType<KvObject>())
                        {
                            if (root.GetString("_class") == "AnimationList")
                                entries.Items.AddRange(((KvArray)root["children"]).Items);
                            else
                                entries.Items.Add(root);
                        }
                        var prefab = new KvObject { ["_class"] = new KvString("Folder"), ["children"] = entries };
                        Expand(prefab, active);
                        child = prefab;
                        children.Items[i] = child;
                    }
                    active.Remove(path);
                }
                Expand(child, active);
            }
        }

        void Visit(KvObject node)
        {
            if (node.GetOrNull("disabled") is KvBool { Value: true }) return;
            if (node.GetString("_class") == "AnimFile")
            {
                var filename = node.GetString("source_filename") ?? throw new FormatException("Animation has no source filename.");
                var take = (long)Number(node.GetOrNull("take"), 0);
                var key = (filename.ToLowerInvariant(), take);
                if (!sources.TryGetValue(key, out var source))
                {
                    source = new Source { Name = "source_" + sources.Count.ToString("D4"), Filename = filename, Take = take };
                    sources.Add(key, source);
                }
                source.Users.Add(node);
            }
            if (node.GetOrNull("children") is KvArray children)
                foreach (var child in children.Items.OfType<KvObject>()) Visit(child);
        }
    }

    /// <summary>The engine imports raw FBX/DMX takes, before additive operations or constraints.
    /// A 1 fps companion measures native frame count without depending on an importer-specific API.</summary>
    public string SamplingModel(string shippedVmdl)
    {
        var doc = Kv3.Parse(shippedVmdl);
        var root = (KvObject)((KvObject)doc.Root)["rootNode"];
        root["anim_graph_name"] = new KvString("");
        var children = Children(doc);
        children.Items.RemoveAll(n => n is KvObject o && o.GetString("_class") is "AnimationList" or "AnimConstraintList");
        var entries = new KvArray();
        foreach (var source in Sources)
        foreach (var countFrames in new[] { false, true })
            entries.Items.Add(new KvObject
            {
                ["_class"] = new KvString("AnimFile"),
                ["name"] = new KvString(source.Name + (countFrames ? "_frames" : "")),
                ["source_filename"] = new KvString(source.Filename),
                ["take"] = new KvLong(source.Take),
                ["start_frame"] = new KvLong(-1), ["end_frame"] = new KvLong(-1),
                ["framerate"] = new KvDouble(countFrames ? 1 : -1),
                ["looping"] = new KvBool(false), ["enable_scale"] = new KvBool(true),
                ["disable_compression"] = new KvBool(true)
            });
        children.Items.Add(new KvObject { ["_class"] = new KvString("AnimationList"), ["children"] = entries,
            ["default_root_bone_name"] = new KvString("pelvis") });
        return Kv3.Serialize(doc);
    }

    private static KvArray Children(Kv3Document doc) => (KvArray)((KvObject)((KvObject)doc.Root)["rootNode"])["children"];
    private static bool ContainsAnimations(KvObject node) => node.GetString("_class") is "AnimationList" or "AnimFile"
        || node.GetOrNull("children") is KvArray children && children.Items.OfType<KvObject>().Any(ContainsAnimations);
    private static double Number(KvValue? value, double fallback) => value switch
    { KvLong n => n.Value, KvDouble n => n.Value, _ => fallback };
}
