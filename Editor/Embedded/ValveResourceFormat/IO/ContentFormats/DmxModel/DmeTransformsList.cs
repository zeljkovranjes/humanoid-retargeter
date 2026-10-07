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
/// Represents a list of transform elements.
/// </summary>
[CamelCaseProperties]
public class DmeTransformsList : DMElement
{
    /// <summary>
    /// List of <see cref="DmeTransform"/> elements.
    /// </summary>
    public HumanoidRetargeter.EditorTools.Embedded.Datamodel.ElementArray Transforms { get; } = [];
}
