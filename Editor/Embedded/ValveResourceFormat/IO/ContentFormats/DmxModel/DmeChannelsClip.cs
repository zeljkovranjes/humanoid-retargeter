#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.Utils;
using HumanoidRetargeter.EditorTools.Embedded.Datamodel.Format;
using DMElement = HumanoidRetargeter.EditorTools.Embedded.Datamodel.Element;

#nullable disable
#pragma warning disable CA2227 // Collection properties should be read only

namespace HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.IO.ContentFormats.DmxModel;

/// <summary>
/// Represents an animation clip with channels.
/// </summary>
[CamelCaseProperties]
public class DmeChannelsClip : DMElement
{
    /// <summary>
    /// Gets the time frame of the clip.
    /// </summary>
    public DmeTimeFrame TimeFrame { get; } = [];

    /// <summary>
    /// Gets or sets the color for display purposes.
    /// </summary>
    public HumanoidRetargeter.EditorTools.Embedded.Datamodel.Color Color { get; set; }

    /// <summary>
    /// Gets or sets the text description.
    /// </summary>
    public string Text { get; set; } = "";

    /// <summary>
    /// Gets or sets a value indicating whether the clip is muted.
    /// </summary>
    public bool Mute { get; set; }

    /// <summary>
    /// Gets the track groups.
    /// </summary>
    public HumanoidRetargeter.EditorTools.Embedded.Datamodel.ElementArray TrackGroups { get; } = [];

    /// <summary>
    /// Gets or sets the display scale.
    /// </summary>
    public float DisplayScale { get; set; } = 1f;

    /// <summary>
    /// Gets the animation channels.
    /// </summary>
    public HumanoidRetargeter.EditorTools.Embedded.Datamodel.ElementArray Channels { get; } = [];

    /// <summary>
    /// Gets or sets the frame rate in frames per second.
    /// </summary>
    public float FrameRate { get; set; } = 30f;
}
