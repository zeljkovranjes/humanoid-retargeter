using System;
using System.Collections.Generic;
using System.Globalization;

namespace HumanoidRetargeter.Target;

/// <summary>
/// One AnimEvent to attach as a child of a vmdl AnimFile node. The node shape replicates the
/// shipped citizen animation list prefab exactly (all 28 shipped events are
/// <c>AE_FOOTSTEP</c>): <c>_class = "AnimEvent"</c>, <c>event_class</c>, <c>event_frame</c>
/// (integer frame on the clip's grid), and an <c>event_keys</c> object carrying
/// <c>Attachment</c> (<c>"foot_L"</c>/<c>"foot_R"</c>), <c>Foot</c> (STRING <c>"0"</c> =
/// left, <c>"1"</c> = right) and <c>Volume</c> (<c>0.7</c> throughout the shipped data).
/// </summary>
public sealed class AnimEventEntry
{
    /// <summary>Event class name (e.g. <c>AE_FOOTSTEP</c>).</summary>
    public string EventClass { get; set; } = "";

    /// <summary>Frame the event fires on (the clip's own frame grid).</summary>
    public int Frame { get; set; }

    /// <summary>event_keys <c>Attachment</c> value (<c>"foot_L"</c>/<c>"foot_R"</c> in the
    /// shipped footstep data); null omits the key.</summary>
    public string? Attachment { get; set; }

    /// <summary>event_keys <c>Foot</c> value — the shipped data encodes the side as a STRING:
    /// <c>"0"</c> = left, <c>"1"</c> = right; null omits the key.</summary>
    public string? Foot { get; set; }

    /// <summary>event_keys <c>Volume</c> value (<c>0.7</c> in all shipped footstep events);
    /// null omits the key.</summary>
    public double? Volume { get; set; }
}

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

    /// <summary>AnimEvent children to emit on the AnimFile node (e.g. generated
    /// <c>AE_FOOTSTEP</c> events); empty = no event nodes.</summary>
    public IReadOnlyList<AnimEventEntry> Events { get; set; } = Array.Empty<AnimEventEntry>();
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
    /// on <paramref name="motionRootBone"/> is included; <see cref="AnimEntry.Events"/>
    /// become AnimEvent children (shipped-prefab node shape, see
    /// <see cref="AnimEventEntry"/>).
    /// </summary>
    internal static KvObject BuildAnimFileNode(AnimEntry entry, string motionRootBone)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var node = new KvObject
        {
            ["_class"] = new KvString("AnimFile"),
            ["name"] = new KvString(entry.Name),
        };

        var nodeChildren = new KvArray();
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
            nodeChildren.Items.Add(extract);
        }
        foreach (var animEvent in entry.Events)
            nodeChildren.Items.Add(BuildAnimEventNode(animEvent));
        if (nodeChildren.Items.Count > 0)
            node["children"] = nodeChildren;

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

    /// <summary>
    /// Builds one AnimEvent KV3 node in the shipped citizen prefab shape:
    /// <c>_class</c>/<c>event_class</c>/<c>event_frame</c> plus an <c>event_keys</c> object
    /// (key order <c>Attachment</c>, <c>Foot</c>, <c>Volume</c>, exactly like the shipped
    /// data; null-valued keys are omitted, and an all-null key set omits the object).
    /// </summary>
    private static KvObject BuildAnimEventNode(AnimEventEntry animEvent)
    {
        var node = new KvObject
        {
            ["_class"] = new KvString("AnimEvent"),
            ["event_class"] = new KvString(animEvent.EventClass),
            ["event_frame"] = new KvLong(animEvent.Frame),
        };

        var keys = new KvObject();
        if (animEvent.Attachment is not null)
            keys["Attachment"] = new KvString(animEvent.Attachment);
        if (animEvent.Foot is not null)
            keys["Foot"] = new KvString(animEvent.Foot);
        if (animEvent.Volume is { } volume)
            keys["Volume"] = new KvDouble(volume);
        if (keys.Count > 0)
            node["event_keys"] = keys;

        return node;
    }
}
