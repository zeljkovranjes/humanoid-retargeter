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
/// Represents a mesh with vertex data and face sets.
/// </summary>
[CamelCaseProperties]
public class DmeMesh : DmeShape
{
    /// <summary>
    /// Gets or sets the bind state of the mesh.
    /// </summary>
    public DMElement BindState { get; set; }

    /// <summary>
    /// Gets or sets the current state of the mesh.
    /// </summary>
    public DMElement CurrentState { get; set; }

    /// <summary>
    /// Gets the base states of the mesh.
    /// </summary>
    public HumanoidRetargeter.EditorTools.Embedded.Datamodel.ElementArray BaseStates { get; } = [];

    /// <summary>
    /// Gets the delta states for morph targets.
    /// </summary>
    public HumanoidRetargeter.EditorTools.Embedded.Datamodel.ElementArray DeltaStates { get; } = [];

    /// <summary>
    /// Gets the face sets that define material groups.
    /// </summary>
    public HumanoidRetargeter.EditorTools.Embedded.Datamodel.ElementArray FaceSets { get; } = [];

    /// <summary>
    /// Gets the delta state weights for morph targets.
    /// </summary>
    public HumanoidRetargeter.EditorTools.Embedded.Datamodel.Vector2Array DeltaStateWeights { get; } = [];

    /// <summary>
    /// Gets the lagged delta state weights for morph targets.
    /// </summary>
    public HumanoidRetargeter.EditorTools.Embedded.Datamodel.Vector2Array DeltaStateWeightsLagged { get; } = [];
}
