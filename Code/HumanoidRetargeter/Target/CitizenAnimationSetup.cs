#nullable enable annotations

using System;
using System.Linq;
using HumanoidRetargeter.Maths;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Target;

/// <summary>Reuses the shipped animation setup, never the shipped character's mesh or materials.</summary>
public static class CitizenAnimationSetup
{
    internal const string ConstraintFolder = "HumanoidRetargeter_CitizenConstraints";
    // Physics, body groups, LODs and materials belong to the custom mesh, not its armature.
    private static readonly string[] Categories =
    {
        "AnimationList", "AnimConstraintList", "BoneMarkupList", "AttachmentList",
        "IKData", "PoseParamList", "WeightListList", "GameDataList"
    };

    /// <summary>Both skeletons must be compiled, in engine space. Names alone are not sufficient.</summary>
    public static string? CompatibilityError(SkeletonModel custom, SkeletonModel reference)
    {
        foreach (var bone in reference.Bones)
        {
            var index = custom.IndexOf(bone.Name);
            // Facial joints are optional on custom meshes. If present, they must still match.
            if (index < 0 && (bone.Name.StartsWith("face_", StringComparison.Ordinal)
                || bone.Name.StartsWith("eye_", StringComparison.Ordinal)
                || bone.Name.StartsWith("ear_", StringComparison.Ordinal)))
                continue;
            if (index < 0)
                return $"Missing bone '{bone.Name}'.";
            var actual = custom[index];
            var parent = actual.ParentIndex < 0 ? null : custom[actual.ParentIndex].Name;
            var expectedParent = bone.ParentIndex < 0 ? null : reference[bone.ParentIndex].Name;
            if (parent != expectedParent)
                return $"Bone '{bone.Name}' has a different parent.";
            var distance = System.Numerics.Vector3.Distance(actual.RestLocal.Pos, bone.RestLocal.Pos);
            var angle = MathQ.AngleBetween(actual.RestLocal.Rot, bone.RestLocal.Rot);
            // Small FBX/compiler rounding differences are acceptable, altered proportions are not.
            if (!float.IsFinite(distance) || !float.IsFinite(angle) || distance > 0.05f || angle > 0.035f)
                return $"Bone '{bone.Name}' has a different bind pose or scale; stock animations require retargeting.";
        }
        return null;
    }

    /// <summary>Attaches the complete stock setup to a standalone custom model. Conflicts fail closed.</summary>
    public static string Apply(string customVmdl, string shippedVmdl)
    {
        var document = Kv3.Parse(customVmdl);
        var root = (KvObject)((KvObject)document.Root)["rootNode"];
        var shipped = (KvObject)((KvObject)Kv3.Parse(shippedVmdl).Root)["rootNode"];
        var graph = shipped.GetString("anim_graph_name");
        if (string.IsNullOrEmpty(graph))
            throw new InvalidOperationException("The shipped model has no animation graph.");
        if (!string.IsNullOrEmpty(root.GetString("base_model_name")))
            throw new InvalidOperationException("Use a standalone custom model, not an animation-only Base Model.");
        var existingGraph = root.GetString("anim_graph_name");
        if (!string.IsNullOrEmpty(existingGraph) && existingGraph != graph)
            throw new InvalidOperationException("The custom model already uses a different animation graph.");
        var children = (KvArray)root["children"];
        var shippedChildren = (KvArray)shipped["children"];
        // Stock animation sources are in cm. A differently scaled VMDL would stretch the mesh.
        double Scale(KvArray nodes)
        {
            var modifiers = nodes.Items.OfType<KvObject>().FirstOrDefault(n => n.GetString("_class") == "ModelModifierList");
            if (modifiers?.GetOrNull("children") is not KvArray items) return 1;
            var scale = 1.0;
            foreach (var modifier in items.Items.OfType<KvObject>())
            {
                if (modifier.GetString("_class") != "ModelModifier_ScaleAndMirror")
                    throw new InvalidOperationException("Unsupported model modifier for stock Citizen animations.");
                if (modifier.Keys.Any(k => modifier[k] is KvBool b && b.Value))
                    throw new InvalidOperationException("Mirrored models require animation retargeting.");
                scale *= modifier["scale"] is KvDouble d ? d.Value : ((KvLong)modifier["scale"]).Value;
            }
            return scale;
        }
        if (Math.Abs(Scale(children) - Scale(shippedChildren)) > 0.000001)
            throw new InvalidOperationException("The custom VMDL must use the Citizen source scale. Import its source mesh instead.");
        foreach (var category in Categories)
        {
            var source = shippedChildren.Items.OfType<KvObject>().SingleOrDefault(n => n.GetString("_class") == category);
            if (source is null) continue;
            if (category == "AnimConstraintList" && source.GetOrNull("children") is KvArray stockConstraints
                && stockConstraints.Items.OfType<KvObject>().Any(n => n.GetString("name") == "CopyPinky"))
            {
                var constraints = new KvArray();
                constraints.Items.Add(new KvObject
                {
                    ["_class"] = new KvString("Folder"),
                    ["name"] = new KvString(ConstraintFolder),
                    ["children"] = source["children"],
                });
                source["children"] = constraints;
            }
            var existing = children.Items.OfType<KvObject>().SingleOrDefault(n => n.GetString("_class") == category);
            if (existing is not null)
            {
                if (KvValue.DeepEquals(existing, source)) continue;
                if (existing.GetOrNull("children") is KvArray entries && entries.Items.Count > 0)
                    throw new InvalidOperationException($"The custom model already has {category} settings. Use its source mesh to create a new Citizen-ready model.");
                children.Items.Remove(existing);
            }
            // Includes Human's inline CopyPinky constraints as well as all animation prefabs.
            children.Items.Add(source);
        }
        root["anim_graph_name"] = new KvString(graph);
        return Kv3.Serialize(document);
    }
}
