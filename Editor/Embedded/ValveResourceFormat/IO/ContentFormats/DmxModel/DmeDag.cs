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
/// Represents a directed acyclic graph node for model hierarchy.
/// </summary>
[CamelCaseProperties]
public class DmeDag : DMElement
{
    /// <summary>
    /// Gets the transform of this DAG node.
    /// </summary>
    public DmeTransform Transform { get; } = [];

    /// <summary>
    /// Gets the mesh shape of this DAG node.
    /// </summary>
    public DmeShape Shape { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether this node is visible.
    /// </summary>
    public bool Visible { get; set; } = true;

    /// <summary>
    /// Gets the child DAG nodes.
    /// </summary>
    public HumanoidRetargeter.EditorTools.Embedded.Datamodel.ElementArray Children { get; } = [];
}
