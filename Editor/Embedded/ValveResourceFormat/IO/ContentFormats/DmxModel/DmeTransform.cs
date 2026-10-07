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
/// Represents a transformation element with position and orientation.
/// </summary>
[CamelCaseProperties]
public class DmeTransform : DMElement
{
    /// <summary>
    /// Gets or sets the position in 3D space.
    /// </summary>
    public global::System.Numerics.Vector3 Position { get; set; } = global::System.Numerics.Vector3.Zero;

    /// <summary>
    /// Gets or sets the orientation as a quaternion.
    /// </summary>
    public global::System.Numerics.Quaternion Orientation { get; set; } = global::System.Numerics.Quaternion.Identity;
}
