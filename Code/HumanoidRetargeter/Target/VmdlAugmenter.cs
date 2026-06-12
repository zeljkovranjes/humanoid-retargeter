using System;
using System.Collections.Generic;
using System.Linq;

namespace HumanoidRetargeter.Target;

/// <summary>
/// Thrown by <see cref="VmdlAugmenter.Augment"/> when an animation name collides with an
/// existing AnimationList node that is not an AnimFile (replacing it would destroy user data).
/// </summary>
public sealed class VmdlAugmentException : Exception
{
    /// <summary>One message per colliding entry, naming the sequence and the existing
    /// node's class.</summary>
    public IReadOnlyList<string> Collisions { get; }

    /// <summary>Creates the exception from the collected collision messages.</summary>
    public VmdlAugmentException(IReadOnlyList<string> collisions)
        : base("Cannot augment vmdl, name collisions with non-AnimFile nodes: "
            + string.Join("; ", collisions))
    {
        Collisions = collisions;
    }
}

/// <summary>Options for <see cref="VmdlAugmenter.Augment"/>.</summary>
public sealed class AugmentOptions
{
    /// <summary>
    /// <c>default_root_bone_name</c> used when the vmdl has NO AnimationList yet (the value
    /// also seeds ExtractMotion nodes' <c>root_bone_name</c>) — same value the standalone
    /// writer uses (<see cref="RetargetTargetSpec.DefaultRootBone"/>). An existing
    /// AnimationList's own non-empty value always wins.
    /// </summary>
    public string DefaultRootBone { get; init; } = "";

    /// <summary>
    /// When true, every CopyPinky ring→pinky constraint in the vmdl (the citizen base
    /// model's transitional <c>AnimConstraintOrient</c> folder, or any constraint whose
    /// driven bone is a <c>finger_pinky_*</c>) gets its weights set to 0, so the exported
    /// pinky channels are no longer overridden at runtime. Idempotent; everything else in
    /// the document is untouched.
    /// </summary>
    public bool NeutralizePinkyConstraints { get; init; }
}

/// <summary>
/// Non-destructively splices AnimFile nodes into an existing vmdl's AnimationList. The rest
/// of the document tree is preserved (semantically — the file is re-serialized through
/// <see cref="Kv3"/>). Re-running with the same entries replaces the previously spliced
/// nodes, making augmentation idempotent.
/// </summary>
public static class VmdlAugmenter
{
    /// <summary>
    /// Returns <paramref name="vmdlText"/> with one AnimFile per entry inserted into the
    /// RootNode's AnimationList (created and appended when absent). Entries whose name
    /// matches an existing AnimFile replace it in place; a name match against any other
    /// node class throws <see cref="VmdlAugmentException"/> before anything is modified.
    /// </summary>
    /// <param name="vmdlText">The current vmdl file content.</param>
    /// <param name="anims">Animations to insert.</param>
    /// <param name="backupOfOriginal">Receives <paramref name="vmdlText"/> verbatim so
    /// callers can write a backup before overwriting the file.</param>
    /// <param name="options">Optional behavior knobs; null = defaults.</param>
    /// <exception cref="FormatException">Thrown when the text is not parseable KV3 or has no
    /// rootNode object.</exception>
    /// <exception cref="VmdlAugmentException">Thrown on name collisions with non-AnimFile
    /// nodes.</exception>
    public static string Augment(string vmdlText, IEnumerable<AnimEntry> anims,
        out string backupOfOriginal, AugmentOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(vmdlText);
        ArgumentNullException.ThrowIfNull(anims);
        backupOfOriginal = vmdlText;
        options ??= new AugmentOptions();

        var entries = anims.ToList();
        var doc = Kv3.Parse(vmdlText);
        if (doc.Root is not KvObject root || root.GetOrNull("rootNode") is not KvObject rootNode)
            throw new FormatException("vmdl has no rootNode object.");

        if (rootNode.GetOrNull("children") is not KvArray children)
        {
            children = new KvArray();
            rootNode["children"] = children;
        }

        var animList = children.Items.OfType<KvObject>()
            .FirstOrDefault(o => o.GetString("_class") == "AnimationList");
        if (animList is null)
        {
            animList = new KvObject
            {
                ["_class"] = new KvString("AnimationList"),
                ["children"] = new KvArray(),
                ["default_root_bone_name"] = new KvString(options.DefaultRootBone),
            };
            children.Items.Add(animList);
        }

        if (animList.GetOrNull("children") is not KvArray listChildren)
        {
            listChildren = new KvArray();
            animList["children"] = listChildren;
        }

        var motionRootBone = animList.GetString("default_root_bone_name") ?? "";
        if (motionRootBone.Length == 0)
            motionRootBone = options.DefaultRootBone;

        // Validate all entries first so a collision throws before any mutation.
        var collisions = new List<string>();
        foreach (var entry in entries)
        {
            var existing = FindByName(listChildren, entry.Name);
            if (existing is not null && existing.GetString("_class") != "AnimFile")
            {
                collisions.Add(
                    $"'{entry.Name}' already exists as {existing.GetString("_class") ?? "<unknown class>"}");
            }
        }
        if (collisions.Count > 0)
            throw new VmdlAugmentException(collisions);

        foreach (var entry in entries)
        {
            var node = VmdlWriter.BuildAnimFileNode(entry, motionRootBone);
            var index = IndexByName(listChildren, entry.Name);
            if (index >= 0)
                listChildren.Items[index] = node; // idempotent re-run: replace same-named AnimFile
            else
                listChildren.Items.Add(node);
        }

        if (options.NeutralizePinkyConstraints)
            NeutralizePinky(rootNode);

        return Kv3.Serialize(doc);
    }

    // ---------------------------------------------------------------- CopyPinky neutralization

    /// <summary>
    /// Zeroes the weights of every pinky-driving constraint reachable from the root node:
    /// the citizen vmdl's CopyPinky Folder (a folder of AnimConstraintOrient nodes copying
    /// finger_ring_* onto finger_pinky_*) and any other AnimConstraint* node whose
    /// AnimConstraintSlave drives a <c>finger_pinky_*</c> bone. Other weights in the
    /// document (e.g. WeightList entries) are never touched.
    /// </summary>
    private static void NeutralizePinky(KvObject node)
    {
        var cls = node.GetString("_class") ?? "";

        var isCopyPinkyFolder = cls == "Folder"
            && string.Equals(node.GetString("name"), "CopyPinky", StringComparison.Ordinal);
        var isPinkyConstraint = cls.StartsWith("AnimConstraint", StringComparison.Ordinal)
            && cls != "AnimConstraintList"
            && DrivesPinkyBone(node);

        if (isCopyPinkyFolder || isPinkyConstraint)
        {
            ZeroWeights(node);
            return; // whole subtree handled
        }

        if (node.GetOrNull("children") is KvArray children)
        {
            foreach (var child in children.Items.OfType<KvObject>())
                NeutralizePinky(child);
        }
    }

    /// <summary>Whether any descendant AnimConstraintSlave drives a finger_pinky_* bone.</summary>
    private static bool DrivesPinkyBone(KvObject node)
    {
        if (node.GetString("_class") == "AnimConstraintSlave"
            && (node.GetString("parent_bone") ?? "")
                .StartsWith("finger_pinky_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (node.GetOrNull("children") is KvArray children)
        {
            foreach (var child in children.Items.OfType<KvObject>())
            {
                if (DrivesPinkyBone(child))
                    return true;
            }
        }
        return false;
    }

    /// <summary>Sets every <c>weight</c> attribute in the subtree to 0.0 (idempotent).</summary>
    private static void ZeroWeights(KvObject node)
    {
        if (node.GetOrNull("weight") is not null)
            node["weight"] = new KvDouble(0.0);

        if (node.GetOrNull("children") is KvArray children)
        {
            foreach (var child in children.Items.OfType<KvObject>())
                ZeroWeights(child);
        }
    }

    private static KvObject? FindByName(KvArray items, string name)
    {
        var index = IndexByName(items, name);
        return index >= 0 ? (KvObject)items.Items[index] : null;
    }

    private static int IndexByName(KvArray items, string name)
    {
        for (var i = 0; i < items.Items.Count; i++)
        {
            if (items.Items[i] is KvObject o
                && string.Equals(o.GetString("name"), name, StringComparison.Ordinal))
            {
                return i;
            }
        }
        return -1;
    }
}
