#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;

namespace HumanoidRetargeter.Target;

/// <summary>An entry graph with its bone references renamed, the subgraph copies it now points at (keyed by
/// asset path) and the subgraphs that could not be read (still referenced as they were).</summary>
public sealed record SmartPortGraphCopy(string Graph, IReadOnlyDictionary<string, string> Subgraphs, IReadOnlyList<string> Unread);

/// <summary>Bone renaming across a graph and the subgraphs it uses, for Smart Port onto a renamed skeleton.</summary>
public static class SmartPortGraphs
{
    /// <summary>
    /// Renames bone references - any string value equal to a renamed source bone, as
    /// <see cref="SmartPortSetup.Rebase"/> does for the entry graph - in a graph and every subgraph it reaches.
    /// The stock Citizen graph keeps its logic in subgraphs that name bones too (the hips of its foot IK), so
    /// renaming the entry graph alone leaves them pointing at bones the ported model no longer has. A subgraph
    /// whose text changes, directly or through a subgraph below it, is copied to <paramref name="subgraphFolder"/>
    /// and its parent points at the copy; shipped subgraphs are never modified. Subgraphs
    /// <paramref name="readSubgraph"/> cannot supply stay referenced as they are and are listed in
    /// <see cref="SmartPortGraphCopy.Unread"/>.
    /// </summary>
    public static SmartPortGraphCopy Rebase(string graph, IReadOnlyDictionary<string, string> boneNames,
        Func<string, string?> readSubgraph, string subgraphFolder)
    {
        var renames = boneNames.Where(p => !string.Equals(p.Key, p.Value, StringComparison.Ordinal))
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var folder = subgraphFolder.Replace('\\', '/').TrimEnd('/');
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var copies = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var unread = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var doc = Kv3.Parse(graph);
        Rewrite(doc.Root, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        return new SmartPortGraphCopy(Kv3.Serialize(doc), outputs, unread.ToArray());

        bool Rewrite(KvValue value, HashSet<string> chain)
        {
            var changed = false;
            if (value is KvObject node)
            {
                foreach (var key in node.Keys.ToArray())
                {
                    if (node[key] is not KvString text)
                    {
                        changed |= Rewrite(node[key], chain);
                        continue;
                    }
                    var replacement = key == "m_subGraphFilename" ? Subgraph(text.Value, chain) : Rename(text.Value);
                    if (replacement is null) continue;
                    node[key] = new KvString(replacement);
                    changed = true;
                }
            }
            else if (value is KvArray array)
            {
                for (var i = 0; i < array.Items.Count; i++)
                {
                    if (array.Items[i] is not KvString text)
                    {
                        changed |= Rewrite(array.Items[i], chain);
                        continue;
                    }
                    if (Rename(text.Value) is not { } replacement) continue;
                    array.Items[i] = new KvString(replacement);
                    changed = true;
                }
            }
            return changed;
        }

        string? Rename(string value) => renames.TryGetValue(value, out var renamed) ? renamed : null;

        // The path of the subgraph copy to use instead of reference, or null to keep the reference.
        string? Subgraph(string reference, HashSet<string> chain)
        {
            var path = reference.Replace('\\', '/');
            if (path.Length == 0 || chain.Contains(path)) return null;
            if (!copies.TryGetValue(path, out var copy))
            {
                copy = null;
                if (readSubgraph(path) is not { } text) unread.Add(path);
                else
                {
                    var sub = Kv3.Parse(text);
                    chain.Add(path);
                    var changed = Rewrite(sub.Root, chain);
                    chain.Remove(path);
                    if (changed)
                    {
                        copy = CopyPath(path);
                        outputs[copy] = Kv3.Serialize(sub);
                    }
                }
                copies[path] = copy;
            }
            return copy;
        }

        string CopyPath(string reference)
        {
            var name = reference[(reference.LastIndexOf('/') + 1)..];
            var stem = name.Contains('.') ? name[..name.LastIndexOf('.')] : name;
            var extension = name[stem.Length..];
            var candidate = (folder.Length > 0 ? folder + "/" : "") + name;
            for (var suffix = 2; outputs.ContainsKey(candidate); suffix++)
                candidate = (folder.Length > 0 ? folder + "/" : "") + stem + "_" + suffix + extension;
            return candidate;
        }
    }
}
