#nullable enable annotations

namespace HumanoidRetargeter.Core.Target;

/// <summary>
/// The source files an AnimGraph2 graph plays from, as KV3 text (their compiler rejects JSON): a skeleton
/// (<c>.skel</c>) listing the bones of a model's source art, and a clip (<c>.anim</c>) per animation, compiled
/// against that skeleton.
/// </summary>
public static class AnimGraph2Sources
{
    /// <summary>The KV3 header both source kinds are written with.</summary>
    public const string GenericHeader =
        "<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:generic:version{7412167c-06e9-4698-aff2-e63eb59037e7} -->";

    /// <summary>A <c>.skel</c> with the bones of <paramref name="sourceFile"/> (an FBX, DMX or SMD), scaled by
    /// <paramref name="globalScale"/>.</summary>
    public static string Skeleton(string sourceFile, double globalScale = 1.0)
        => Kv3.Serialize(new Kv3Document(GenericHeader, new KvObject
        {
            ["m_sourceFilename"] = new KvString(sourceFile),
            ["m_flGlobalScale"] = new KvDouble(globalScale),
            ["m_additionalSourceFilenames"] = new KvArray(),
            ["m_rootBoneName"] = new KvString(""),
        }));

    /// <summary>An <c>.anim</c> playing <paramref name="sourceFile"/>'s first take on <paramref name="skeleton"/>
    /// (a <c>.skel</c>), scaled by <paramref name="importScale"/>.</summary>
    public static string Clip(string sourceFile, string skeleton, double importScale = 1.0)
        => Kv3.Serialize(new Kv3Document(GenericHeader, new KvObject
        {
            ["_class"] = new KvString("CClipDocument"),
            ["m_sourceFilename"] = new KvString(sourceFile),
            ["m_animationSkeletonName"] = new KvString(skeleton),
            ["m_nTake"] = new KvLong(0),
            ["m_flImportScale"] = new KvDouble(importScale),
        }));
}
