#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeter.Maths;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Target;

/// <summary>Transfers a compatible model's setup without replacing the destination character.</summary>
public static class SmartPortSetup
{
    // These describe the target geometry, not the source animation system. Everything else
    // comes from the source, including unfamiliar extension nodes (never silently discarded).
    private static readonly HashSet<string> TargetCategories = new(StringComparer.Ordinal)
    {
        "RenderMeshList", "MaterialGroupList", "BodyGroupList", "LODGroupList",
        "PhysicsShapeList", "PhysicsJointList", "HitboxSetList", "Skeleton",
        "MorphControlList", "MorphRuleList", "MorphGroupList", "ModelModifierList"
    };

    /// <summary>Direct transfer requires all source bones, their hierarchy and bind pose.</summary>
    public static string? CompatibilityError(SkeletonModel target, SkeletonModel source)
    {
        if (source.Count == 0 || target.Count == 0) return "Both models need a compiled skeleton.";
        foreach (var bone in source.Bones)
        {
            var index = target.IndexOf(bone.Name);
            if (index < 0) return $"Target is missing source bone '{bone.Name}'.";
            var actual = target[index];
            var parent = actual.ParentIndex < 0 ? null : target[actual.ParentIndex].Name;
            var expected = bone.ParentIndex < 0 ? null : source[bone.ParentIndex].Name;
            if (parent != expected) return $"Bone '{bone.Name}' has a different parent.";
            var distance = System.Numerics.Vector3.Distance(actual.RestLocal.Pos, bone.RestLocal.Pos);
            var angle = MathQ.AngleBetween(actual.RestLocal.Rot, bone.RestLocal.Rot);
            if (!float.IsFinite(distance) || !float.IsFinite(angle)) return $"Invalid bind transform on '{bone.Name}'.";
            if (distance > .05f || angle > .035f)
                return $"Bone '{bone.Name}' has a different bind pose. Use retargeting rather than direct Smart Port for this pair.";
        }
        return null;
    }

    /// <summary>Both inputs must use the same source space; compiled recoveries use engine units.</summary>
    public static string Apply(string targetVmdl, string sourceVmdl, string graphPath)
    {
        var document = Kv3.Parse(sourceVmdl);
        var source = Root(document);
        var target = Root(Kv3.Parse(targetVmdl));
        if (!string.IsNullOrEmpty(source.GetString("base_model_name")) || !string.IsNullOrEmpty(target.GetString("base_model_name")))
            throw new InvalidOperationException("Smart Port needs standalone models, not animation-only Base Models.");
        if (string.IsNullOrWhiteSpace(graphPath)) throw new InvalidOperationException("Source has no animation graph.");
        var sourceChildren = Children(source);
        var targetChildren = Children(target);
        if (!targetChildren.Items.OfType<KvObject>().Any(n => n.GetString("_class") == "RenderMeshList"))
            throw new InvalidOperationException("The target has no render mesh.");
        var sourceModifiers = sourceChildren.Items.OfType<KvObject>().SingleOrDefault(n => n.GetString("_class") == "ModelModifierList");
        var targetModifiers = targetChildren.Items.OfType<KvObject>().SingleOrDefault(n => n.GetString("_class") == "ModelModifierList");
        if (!KvValue.DeepEquals(sourceModifiers, targetModifiers))
            throw new InvalidOperationException("Model source spaces differ. Recover both compiled models before porting.");
        sourceChildren.Items.RemoveAll(n => n is KvObject o && TargetCategories.Contains(o.GetString("_class") ?? ""));
        sourceChildren.Items.AddRange(targetChildren.Items.OfType<KvObject>().Where(n => TargetCategories.Contains(n.GetString("_class") ?? "")));
        source["anim_graph_name"] = new KvString(graphPath);
        return Kv3.Serialize(document);
    }

    /// <summary>Combines recovered engine-space models after their animation channels have been retargeted.</summary>
    public static string ApplyRetargeted(string targetVmdl, string sourceVmdl, string graphPath, SmartPortRig rig)
    {
        var targetDoc = Kv3.Parse(targetVmdl);
        var target = Children(Root(targetDoc));
        var sourceDoc = Kv3.Parse(Rebase(sourceVmdl, rig.BoneNames));
        var source = Children(Root(sourceDoc));
        KvObject? Category(KvArray nodes, string type) => nodes.Items.OfType<KvObject>().SingleOrDefault(n => n.GetString("_class") == type);
        IEnumerable<KvObject> Descendants(KvObject node)
        {
            yield return node;
            if (node.GetOrNull("children") is KvArray children)
                foreach (var child in children.Items.OfType<KvObject>())
                    foreach (var item in Descendants(child)) yield return item;
        }
        var skeleton = Category(target, "Skeleton") ?? throw new InvalidOperationException("Recovered target has no skeleton.");
        var bones = Descendants(skeleton).Where(n => n.GetString("_class") == "Bone").ToDictionary(n => n.GetString("name")!);
        var originalNames = bones.Keys.ToHashSet(StringComparer.Ordinal);
        var sourceSkeleton = Category(source, "Skeleton") ?? throw new InvalidOperationException("Recovered source has no skeleton.");
        var sourceBones = Descendants(sourceSkeleton).Where(n => n.GetString("_class") == "Bone").ToDictionary(n => n.GetString("name")!);
        foreach (var bone in rig.Target.Bones)
        {
            if (bones.ContainsKey(bone.Name)) continue;
            var node = sourceBones[bone.Name];
            node["children"] = new KvArray();
            var origin = new KvArray();
            foreach (var coordinate in new[] { bone.RestLocal.Pos.X, bone.RestLocal.Pos.Y, bone.RestLocal.Pos.Z }) origin.Items.Add(new KvDouble(coordinate));
            node["origin"] = origin;
            var parent = bone.ParentIndex < 0 ? skeleton : bones[rig.Target[bone.ParentIndex].Name];
            if (parent.GetOrNull("children") is not KvArray) parent["children"] = new KvArray();
            ((KvArray)parent["children"]).Items.Add(node);
            bones.Add(bone.Name, node);
        }
        // Target skin helpers keep their fitted constraints. Source graph-only helpers retain theirs.
        var constraints = Category(source, "AnimConstraintList");
        if (constraints?.GetOrNull("children") is KvArray entries)
            entries.Items.RemoveAll(n => n is KvObject node && Descendants(node).Any(child =>
                originalNames.Contains(child.GetString("constrained_bone") ?? "") ||
                child.GetString("_class") == "AnimConstraintSlave" && originalNames.Contains(child.GetString("parent_bone") ?? "")));
        foreach (var type in new[] { "AnimConstraintList", "AttachmentList" })
        {
            var fitted = Category(target, type);
            if (fitted?.GetOrNull("children") is not KvArray fittedItems) continue;
            var existing = Category(source, type);
            if (existing is null) { source.Items.Add(fitted); continue; }
            var items = (KvArray)existing["children"];
            if (type == "AttachmentList")
            {
                var fittedNames = fittedItems.Items.OfType<KvObject>().Select(n => n.GetString("name")).ToHashSet();
                items.Items.RemoveAll(n => n is KvObject node && fittedNames.Contains(node.GetString("name")));
            }
            items.Items.AddRange(fittedItems.Items);
        }
        return Apply(Kv3.Serialize(targetDoc), Kv3.Serialize(sourceDoc), graphPath);
    }

    /// <summary>Rebases only files actually recovered, leaving installed resource dependencies intact.</summary>
    public static string Rebase(string text, IReadOnlyDictionary<string, string> files)
    {
        var doc = Kv3.Parse(text);
        Visit(doc.Root);
        return Kv3.Serialize(doc);
        void Visit(KvValue value)
        {
            if (value is KvObject obj)
                foreach (var key in obj.Keys.ToArray())
                    if (obj[key] is KvString s && files.TryGetValue(s.Value.Replace('\\', '/'), out var path)) obj[key] = new KvString(path);
                    else Visit(obj[key]);
            else if (value is KvArray array)
                for (var i = 0; i < array.Items.Count; i++)
                    if (array.Items[i] is KvString s && files.TryGetValue(s.Value.Replace('\\', '/'), out var path)) array.Items[i] = new KvString(path);
                    else Visit(array.Items[i]);
        }
    }

    private static KvObject Root(Kv3Document doc) => (doc.Root as KvObject)?.GetOrNull("rootNode") as KvObject
        ?? throw new InvalidOperationException("Expected a ModelDoc VMDL source.");
    private static KvArray Children(KvObject root) => root.GetOrNull("children") as KvArray
        ?? throw new InvalidOperationException("ModelDoc has no node list.");
}
