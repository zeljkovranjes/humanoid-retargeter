#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.Utils;
namespace HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.ResourceTypes.ModelAnimation;

    /// <summary>
    /// Specifies the type of data contained in an animation channel.
    /// </summary>
    public enum AnimationChannelAttribute
    {
#pragma warning disable CS1591
        Position,
        Angle,
        Scale,
        Data,
        Unknown,
#pragma warning restore CS1591
    }
