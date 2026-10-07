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
/// Represents a time frame for animations.
/// </summary>
[CamelCaseProperties]
public class DmeTimeFrame : DMElement
{
    /// <summary>
    /// Gets or sets the start time.
    /// </summary>
    public TimeSpan Start { get; set; }

    /// <summary>
    /// Gets or sets the duration.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Gets or sets the time offset.
    /// </summary>
    public TimeSpan Offset { get; set; }

    /// <summary>
    /// Gets or sets the time scale.
    /// </summary>
    public float Scale { get; set; } = 1f;
}
