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
