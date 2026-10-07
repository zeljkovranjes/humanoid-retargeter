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
/// Represents a face set with associated material.
/// </summary>
[CamelCaseProperties]
public class DmeFaceSet : DMElement
{
    /// <summary>
    /// Gets the array of face indices.
    /// </summary>
    public HumanoidRetargeter.EditorTools.Embedded.Datamodel.IntArray Faces { get; } = [];

    /// <summary>
    /// Gets the material definition associated with this face set.
    /// </summary>
    public DmeMaterial Material { get; } = new() { Name = "material" };

    /// <summary>
    /// Represents a material reference.
    /// </summary>
    public class DmeMaterial : DMElement
    {
        /// <summary>
        /// Gets or sets the material name.
        /// </summary>
        [DMProperty(name: "mtlName")]
        public string MaterialName { get; set; } = string.Empty;
    }
}
