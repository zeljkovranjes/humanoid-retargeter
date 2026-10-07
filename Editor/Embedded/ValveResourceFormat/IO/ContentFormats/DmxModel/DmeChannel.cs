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
/// Represents an animation channel connecting source and target elements.
/// </summary>
[CamelCaseProperties]
public class DmeChannel : DMElement
{
    /// <summary>
    /// Gets or sets the source element.
    /// </summary>
    public DMElement FromElement { get; set; }

    /// <summary>
    /// Gets or sets the source attribute name.
    /// </summary>
    public string FromAttribute { get; set; } = "";

    /// <summary>
    /// Gets or sets the source index.
    /// </summary>
    public int FromIndex { get; set; }

    /// <summary>
    /// Gets or sets the target element.
    /// </summary>
    public DMElement ToElement { get; set; }

    /// <summary>
    /// Gets or sets the target attribute name.
    /// </summary>
    public string ToAttribute { get; set; } = "";

    /// <summary>
    /// Gets or sets the target index.
    /// </summary>
    public int ToIndex { get; set; }

    /// <summary>
    /// Gets or sets the channel mode.
    /// </summary>
    public int Mode { get; set; }

    private DMElement _log;

    /// <summary>
    /// Gets or sets the animation log data.
    /// </summary>
    public DMElement Log
    {
        get
        {
            return _log;
        }
        set
        {
            var logType = value.GetType();
            if (logType.GetGenericTypeDefinition() != typeof(DmeLog<>))
            {
                throw new ArgumentException($"DmeChannel.Log can only contain DmeLog types");
            }

            _log = value;
        }
    }
}
