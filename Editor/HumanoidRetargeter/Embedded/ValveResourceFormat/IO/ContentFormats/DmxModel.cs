#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeterVrf.Utils;
using HumanoidRetargeterDmx.Format;
using DMElement = HumanoidRetargeterDmx.Element;

namespace HumanoidRetargeterVrf.IO.ContentFormats.DmxModel;

#nullable disable

#pragma warning disable CA2227 // Collection properties should be read only
[CamelCaseProperties]
internal class DmeModel : DMElement
{
    public DmeTransform Transform { get; set; } = [];
    public DMElement Shape { get; set; }
    public bool Visible { get; set; } = true;
    public HumanoidRetargeterDmx.ElementArray Children { get; } = [];
    public HumanoidRetargeterDmx.ElementArray JointList { get; set; } = [];

    /// <summary>
    /// List of <see cref="DmeTransformsList"/> elements.
    /// </summary>
    public HumanoidRetargeterDmx.ElementArray BaseStates { get; set; } = [];
    public DmeAxisSystem AxisSystem { get; set; } = [];
}

/// <summary>
/// Represents an abstract shape
/// </summary>
[CamelCaseProperties]
public class DmeShape : DMElement
{
    /// <summary>
    /// Gets or sets a value indicating whether this mesh is visible.
    /// </summary>
    public bool Visible { get; set; } = true;
}

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

/// <summary>
/// Represents the axis system configuration for the model.
/// </summary>
[CamelCaseProperties]
public class DmeAxisSystem : DMElement
{
    /// <summary>
    /// Gets or sets the up axis.
    /// </summary>
    public int UpAxis { get; set; } = 3;

    /// <summary>
    /// Gets or sets the forward parity.
    /// </summary>
    public int ForwardParity { get; set; } = 1;

    /// <summary>
    /// Gets or sets the coordinate system.
    /// </summary>
    public int CoordSys { get; set; }
}

/// <summary>
/// Represents a list of transform elements.
/// </summary>
[CamelCaseProperties]
public class DmeTransformsList : DMElement
{
    /// <summary>
    /// List of <see cref="DmeTransform"/> elements.
    /// </summary>
    public HumanoidRetargeterDmx.ElementArray Transforms { get; } = [];
}

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
    public HumanoidRetargeterDmx.ElementArray Children { get; } = [];
}

/// <summary>
/// Represents a skeletal joint in the model hierarchy.
/// </summary>
[CamelCaseProperties] public class DmeJoint : DmeDag;

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
    public HumanoidRetargeterDmx.ElementArray BaseStates { get; } = [];

    /// <summary>
    /// Gets the delta states for morph targets.
    /// </summary>
    public HumanoidRetargeterDmx.ElementArray DeltaStates { get; } = [];

    /// <summary>
    /// Gets the face sets that define material groups.
    /// </summary>
    public HumanoidRetargeterDmx.ElementArray FaceSets { get; } = [];

    /// <summary>
    /// Gets the delta state weights for morph targets.
    /// </summary>
    public HumanoidRetargeterDmx.Vector2Array DeltaStateWeights { get; } = [];

    /// <summary>
    /// Gets the lagged delta state weights for morph targets.
    /// </summary>
    public HumanoidRetargeterDmx.Vector2Array DeltaStateWeightsLagged { get; } = [];
}

/// <summary>
/// Represents a face set with associated material.
/// </summary>
[CamelCaseProperties]
public class DmeFaceSet : DMElement
{
    /// <summary>
    /// Gets the array of face indices.
    /// </summary>
    public HumanoidRetargeterDmx.IntArray Faces { get; } = [];

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

/// <summary>
/// Represents vertex data with multiple streams.
/// </summary>
[CamelCaseProperties]
public class DmeVertexData : DMElement
{
    /// <summary>
    /// Gets the vertex format specification.
    /// </summary>
    public HumanoidRetargeterDmx.StringArray VertexFormat { get; } = [];

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

/// <summary>
/// Represents a list of animations.
/// </summary>
[CamelCaseProperties]
public class DmeAnimationList : DMElement
{
    /// <summary>
    /// Gets the array of animations.
    /// </summary>
    public HumanoidRetargeterDmx.ElementArray Animations { get; } = [];
}

/// <summary>
/// Represents an animation clip with channels.
/// </summary>
[CamelCaseProperties]
public class DmeChannelsClip : DMElement
{
    /// <summary>
    /// Gets the time frame of the clip.
    /// </summary>
    public DmeTimeFrame TimeFrame { get; } = [];

    /// <summary>
    /// Gets or sets the color for display purposes.
    /// </summary>
    public HumanoidRetargeterDmx.Color Color { get; set; }

    /// <summary>
    /// Gets or sets the text description.
    /// </summary>
    public string Text { get; set; } = "";

    /// <summary>
    /// Gets or sets a value indicating whether the clip is muted.
    /// </summary>
    public bool Mute { get; set; }

    /// <summary>
    /// Gets the track groups.
    /// </summary>
    public HumanoidRetargeterDmx.ElementArray TrackGroups { get; } = [];

    /// <summary>
    /// Gets or sets the display scale.
    /// </summary>
    public float DisplayScale { get; set; } = 1f;

    /// <summary>
    /// Gets the animation channels.
    /// </summary>
    public HumanoidRetargeterDmx.ElementArray Channels { get; } = [];

    /// <summary>
    /// Gets or sets the frame rate in frames per second.
    /// </summary>
    public float FrameRate { get; set; } = 30f;
}

/// <summary>
/// Represents a time frame for animations.
/// </summary>
[CamelCaseProperties]
public class DmeTimeFrame : DMElement
{
    /// <summary>
    /// Gets or sets the start time.
    /// </summary>
    public TimeSpan Start { get; set; }

    /// <summary>
    /// Gets or sets the duration.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Gets or sets the time offset.
    /// </summary>
    public TimeSpan Offset { get; set; }

    /// <summary>
    /// Gets or sets the time scale.
    /// </summary>
    public float Scale { get; set; } = 1f;
}

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

/// <summary>
/// Base class for typed animation logs.
/// </summary>
public abstract class DmeTypedLog<T> : DMElement
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DmeTypedLog{T}"/> class.
    /// </summary>
    protected DmeTypedLog(string namePostfix)
    {
        string typeName;
        if (typeof(T) == typeof(float)) //Name would be 'Single' without this
        {
            typeName = "Float";
        }
        else
        {
            typeName = typeof(T).Name;
        }

        if (char.IsLower(typeName[0]))
        {
            typeName = char.ToUpperInvariant(typeName[0]) + typeName[1..];
        }

        ClassName = $"Dme{typeName}{namePostfix}";
        Name = $"{typeName.ToLowerInvariant()} log";
    }
}

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
    public HumanoidRetargeterDmx.ElementArray Layers { get; set; } = [];

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
    public HumanoidRetargeterDmx.TimeSpanArray BookmarksX { get; } = [];

    /// <summary>
    /// Gets the Y-axis bookmarks.
    /// </summary>
    public HumanoidRetargeterDmx.TimeSpanArray BookmarksY { get; } = [];

    /// <summary>
    /// Gets the Z-axis bookmarks.
    /// </summary>
    public HumanoidRetargeterDmx.TimeSpanArray BookmarksZ { get; } = [];

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
    public HumanoidRetargeterDmx.TimeSpanArray Times { get; set; } = [];

    /// <summary>
    /// Gets the curve interpolation types for each keyframe.
    /// </summary>
    [DMProperty("curvetypes")]
    public HumanoidRetargeterDmx.IntArray CurveTypes { get; } = [];

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
#pragma warning restore CA2227 // Collection properties should be read only
