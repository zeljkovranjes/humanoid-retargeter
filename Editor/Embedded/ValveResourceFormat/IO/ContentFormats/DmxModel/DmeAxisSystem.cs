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
/// Represents the axis system configuration for the model.
/// </summary>
[CamelCaseProperties]
public class DmeAxisSystem : DMElement
{
    /// <summary>
    /// Gets or sets the up axis.
    /// </summary>
    public int UpAxis { get; set; } = 3;

    /// <summary>
    /// Gets or sets the forward parity.
    /// </summary>
    public int ForwardParity { get; set; } = 1;

    /// <summary>
    /// Gets or sets the coordinate system.
    /// </summary>
    public int CoordSys { get; set; }
}
