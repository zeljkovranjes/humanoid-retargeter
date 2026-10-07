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
/// Represents vertex data with multiple streams.
/// </summary>
[CamelCaseProperties]
public class DmeVertexData : DMElement
{
    /// <summary>
    /// Gets the vertex format specification.
    /// </summary>
    public HumanoidRetargeter.EditorTools.Embedded.Datamodel.StringArray VertexFormat { get; } = [];

    /// <summary>
    /// Gets or sets the number of joints for skinning.
    /// </summary>
    public int JointCount { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to flip V texture coordinates.
    /// </summary>
    public bool FlipVCoordinates { get; set; }

    /// <summary>
    /// Adds a vertex data stream.
    /// </summary>
    public void AddStream<T>(string name, T[] data)
    {
        VertexFormat.Add(name);
        this[name] = data;
    }

    /// <summary>
    /// Adds an indexed vertex data stream.
    /// </summary>
    public void AddIndexedStream<T>(string name, T[] data, int[] indices)
    {
        VertexFormat.Add(name);
        this[name] = data;
        this[name + "Indices"] = indices;
    }
}
