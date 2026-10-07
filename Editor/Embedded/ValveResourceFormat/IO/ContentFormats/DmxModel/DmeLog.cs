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
/// Represents an animation log with keyframe data.
/// </summary>
[CamelCaseProperties]
public class DmeLog<T> : DmeTypedLog<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DmeLog{T}"/> class.
    /// </summary>
    public DmeLog() : base("Log") { }

    /// <summary>
    /// Gets or sets the log layers containing keyframe data.
    /// </summary>
    [DMProperty("layers")]
    public HumanoidRetargeter.EditorTools.Embedded.Datamodel.ElementArray Layers { get; set; } = [];

    /// <summary>
    /// Gets or sets the curve interpolation information.
    /// </summary>
    [DMProperty("curveinfo")]
    public DMElement CurveInfo { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to use the default value.
    /// </summary>
    [DMProperty("usedefaultvalue")]
    public bool UseDefaultValue { get; set; }

    /// <summary>
    /// Gets or sets the default value.
    /// </summary>
    [DMProperty("defaultvalue")]
    public T DefaultValue { get; set; }

    /// <summary>
    /// Gets the X-axis bookmarks.
    /// </summary>
    public HumanoidRetargeter.EditorTools.Embedded.Datamodel.TimeSpanArray BookmarksX { get; } = [];

    /// <summary>
    /// Gets the Y-axis bookmarks.
    /// </summary>
    public HumanoidRetargeter.EditorTools.Embedded.Datamodel.TimeSpanArray BookmarksY { get; } = [];

    /// <summary>
    /// Gets the Z-axis bookmarks.
    /// </summary>
    public HumanoidRetargeter.EditorTools.Embedded.Datamodel.TimeSpanArray BookmarksZ { get; } = [];

    /// <summary>
    /// Gets the log layer at the specified index.
    /// </summary>
    public DmeLogLayer<T> GetLayer(int index)
    {
        return (DmeLogLayer<T>)Layers[index];
    }

    /// <summary>
    /// Adds a log layer.
    /// </summary>
    public void AddLayer(DmeLogLayer<T> layer)
    {
        Layers.Add(layer);
    }

    /// <summary>
    /// Gets the number of layers.
    /// </summary>
    public int LayerCount => Layers.Count;
}
