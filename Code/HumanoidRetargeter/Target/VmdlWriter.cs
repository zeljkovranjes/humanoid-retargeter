using System;
using System.Collections.Generic;
using System.Globalization;

namespace HumanoidRetargeter.Target;

/// <summary>One animation to register in a vmdl AnimationList.</summary>
public sealed class AnimEntry
{
    /// <summary>Sequence name (must be unique within the AnimationList).</summary>
    public string Name { get; set; } = "";

    /// <summary>Animation source path relative to the assets root
    /// (e.g. <c>models/x/animations/walk.dmx</c>).</summary>
    public string SourceFilename { get; set; } = "";

    /// <summary>Whether the sequence loops.</summary>
    public bool Looping { get; set; }

    /// <summary>Whether to add an ExtractMotion child node (ground-plane translation
    /// extraction, linear, matching the shipped citizen prefab usage).</summary>
    public bool ExtractMotion { get; set; }
}

/// <summary>
/// Generates standalone Base-Model vmdl files (KV3 text) that reference an existing model and
/// register retargeted animation DMX files, following the shipped citizen vmdl conventions
/// (field set proven to compile in M0 via <c>m0_test.vmdl</c>).
/// </summary>
public static class VmdlWriter
{
    /// <summary>The KV3 header line used by shipped modeldoc vmdl files.</summary>
    public const string Kv3Header =
        "<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc30:version{8c2d7a91-9c42-4bf0-883a-5a3b1762d4f1} -->";

    /// <summary>
    /// Builds a standalone vmdl: RootNode with <c>base_model_name</c> =
    /// <paramref name="baseModelPath"/>, an optional ModelModifierList/ScaleAndMirror node
    /// (omitted entirely when <paramref name="scale"/> is 1.0 — engine-unit sources need no
    /// rescale; 0.3937 converts cm sources like the citizen rig), and an AnimationList with
    /// one AnimFile per entry.
    /// </summary>
    public static string GenerateStandalone(string baseModelPath, IEnumerable<AnimEntry> anims,
        float scale, string defaultRootBone)
    {
        ArgumentNullException.ThrowIfNull(baseModelPath);
        ArgumentNullException.ThrowIfNull(anims);
        ArgumentNullException.ThrowIfNull(defaultRootBone);

        var children = new KvArray();

        if (scale != 1.0f)
        {
            var modifier = new KvObject
            {
                ["_class"] = new KvString("ModelModifier_ScaleAndMirror"),
                // float -> shortest-round-trip string -> double keeps "0.3937" exact in text.
                ["scale"] = new KvDouble(
                    double.Parse(scale.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)),
                ["mirror_x"] = new KvBool(false),
                ["mirror_y"] = new KvBool(false),
                ["mirror_z"] = new KvBool(false),
                ["flip_bone_forward"] = new KvBool(false),
                ["swap_left_and_right_bones"] = new KvBool(false),
            };
            var modifierChildren = new KvArray();
            modifierChildren.Items.Add(modifier);
            children.Items.Add(new KvObject
            {
                ["_class"] = new KvString("ModelModifierList"),
                ["children"] = modifierChildren,
            });
        }

        var animChildren = new KvArray();
        foreach (var anim in anims)
            animChildren.Items.Add(BuildAnimFileNode(anim, defaultRootBone));
        children.Items.Add(new KvObject
        {
            ["_class"] = new KvString("AnimationList"),
            ["children"] = animChildren,
            ["default_root_bone_name"] = new KvString(defaultRootBone),
        });

        var root = new KvObject
        {
            ["rootNode"] = new KvObject
            {
                ["_class"] = new KvString("RootNode"),
                ["children"] = children,
                ["model_archetype"] = new KvString(""),
                ["primary_associated_entity"] = new KvString(""),
                ["anim_graph_name"] = new KvString(""),
                ["base_model_name"] = new KvString(baseModelPath),
            },
        };

        return Kv3.Serialize(new Kv3Document(Kv3Header, root));
    }

    /// <summary>
    /// Builds one AnimFile KV3 node (full attribute set as compiled in M0). When the entry
    /// requests motion extraction, an ExtractMotion child extracting ground-plane translation
    /// on <paramref name="motionRootBone"/> is included.
    /// </summary>
    internal static KvObject BuildAnimFileNode(AnimEntry entry, string motionRootBone)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var node = new KvObject
        {
            ["_class"] = new KvString("AnimFile"),
            ["name"] = new KvString(entry.Name),
        };

        if (entry.ExtractMotion)
        {
            var extract = new KvObject
            {
                ["_class"] = new KvString("ExtractMotion"),
                ["extract_tx"] = new KvBool(true),
                ["extract_ty"] = new KvBool(true),
                ["extract_tz"] = new KvBool(false),
                ["extract_rz"] = new KvBool(false),
                ["linear"] = new KvBool(true),
                ["quadratic"] = new KvBool(false),
                ["root_bone_name"] = new KvString(motionRootBone),
                ["motion_type"] = new KvString("Single"),
            };
            var nodeChildren = new KvArray();
            nodeChildren.Items.Add(extract);
            node["children"] = nodeChildren;
        }

        node["activity_name"] = new KvString("");
        node["activity_weight"] = new KvLong(1);
        node["weight_list_name"] = new KvString("");
        node["fade_in_time"] = new KvDouble(0.2);
        node["fade_out_time"] = new KvDouble(0.2);
        node["looping"] = new KvBool(entry.Looping);
        node["delta"] = new KvBool(false);
        node["worldSpace"] = new KvBool(false);
        node["hidden"] = new KvBool(false);
        node["anim_markup_ordered"] = new KvBool(false);
        node["disable_compression"] = new KvBool(false);
        node["disable_interpolation"] = new KvBool(false);
        node["enable_scale"] = new KvBool(false);
        node["source_filename"] = new KvString(entry.SourceFilename);
        node["start_frame"] = new KvLong(-1);
        node["end_frame"] = new KvLong(-1);
        node["framerate"] = new KvDouble(-1.0);
        node["take"] = new KvLong(0);
        node["reverse"] = new KvBool(false);
        return node;
    }
}
