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
/// Represents a layer of keyframe data in an animation log.
/// </summary>
[CamelCaseProperties]
public class DmeLogLayer<T> : DmeTypedLog<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DmeLogLayer{T}"/> class.
    /// </summary>
    public DmeLogLayer() : base("LogLayer") { }

    /// <summary>
    /// Gets or sets the keyframe times.
    /// </summary>
    public HumanoidRetargeter.EditorTools.Embedded.Datamodel.TimeSpanArray Times { get; set; } = [];

    /// <summary>
    /// Gets the curve interpolation types for each keyframe.
    /// </summary>
    [DMProperty("curvetypes")]
    public HumanoidRetargeter.EditorTools.Embedded.Datamodel.IntArray CurveTypes { get; } = [];

    /// <summary>
    /// Gets or sets the keyframe values.
    /// </summary>
    [DMProperty("values")]
    public T[] LayerValues { get; set; }


    /// <summary>
    /// Checks if this layer only contains default/zero values.
    /// </summary>
    public bool IsLayerZero()
    {
        object defaultValue;

        //quaternions initialize to all 0s
        if (typeof(T) == typeof(global::System.Numerics.Quaternion))
        {
            defaultValue = global::System.Numerics.Quaternion.Identity;
        }
        else
        {
            defaultValue = default(T);
        }

        foreach (var item in LayerValues)
        {
            if (!item.Equals(defaultValue))
            {
                return false;
            }
        }
        return true;
    }
}
