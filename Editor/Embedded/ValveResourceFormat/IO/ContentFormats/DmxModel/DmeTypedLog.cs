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
