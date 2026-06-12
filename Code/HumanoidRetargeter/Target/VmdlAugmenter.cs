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
    /// <exception cref="FormatException">Thrown when the text is not parseable KV3 or has no
    /// rootNode object.</exception>
    /// <exception cref="VmdlAugmentException">Thrown on name collisions with non-AnimFile
    /// nodes.</exception>
    public static string Augment(string vmdlText, IEnumerable<AnimEntry> anims,
        out string backupOfOriginal)
    {
        ArgumentNullException.ThrowIfNull(vmdlText);
        ArgumentNullException.ThrowIfNull(anims);
        backupOfOriginal = vmdlText;

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
                ["default_root_bone_name"] = new KvString(""),
            };
            children.Items.Add(animList);
        }

        if (animList.GetOrNull("children") is not KvArray listChildren)
        {
            listChildren = new KvArray();
            animList["children"] = listChildren;
        }

        var motionRootBone = animList.GetString("default_root_bone_name") ?? "";

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

        return Kv3.Serialize(doc);
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
