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

[CamelCaseProperties]
internal class DmeModel : DMElement
{
    public DmeTransform Transform { get; set; } = [];
    public DMElement Shape { get; set; }
    public bool Visible { get; set; } = true;
    public HumanoidRetargeter.EditorTools.Embedded.Datamodel.ElementArray Children { get; } = [];
    public HumanoidRetargeter.EditorTools.Embedded.Datamodel.ElementArray JointList { get; set; } = [];

    /// <summary>
    /// List of <see cref="DmeTransformsList"/> elements.
    /// </summary>
    public HumanoidRetargeter.EditorTools.Embedded.Datamodel.ElementArray BaseStates { get; set; } = [];
    public DmeAxisSystem AxisSystem { get; set; } = [];
}
