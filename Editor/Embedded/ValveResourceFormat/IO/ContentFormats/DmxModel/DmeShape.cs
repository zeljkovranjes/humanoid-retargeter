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
/// Represents an abstract shape
/// </summary>
[CamelCaseProperties]
public class DmeShape : DMElement
{
    /// <summary>
    /// Gets or sets a value indicating whether this mesh is visible.
    /// </summary>
    public bool Visible { get; set; } = true;
}
