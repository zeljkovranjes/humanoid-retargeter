#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HumanoidRetargeter.EditorTools.Embedded.Datamodel.Format;

/// <summary>
/// This class' property names are mostly m_hungarian.
/// </summary>
public class HungarianPropertiesAttribute : CamelCasePropertiesAttribute
{
    public override string GetAttributeName(string propertyName, Type propertyType)
    {
        var typeAnnotation = propertyType switch
        {
            _ when propertyType == typeof(int) => "n",
            _ when propertyType == typeof(float) => "fl",
            _ when propertyType == typeof(bool) => "b",
            _ when propertyType == typeof(global::System.Numerics.Vector2) => "v",
            _ when propertyType == typeof(global::System.Numerics.Vector3) => "v",
            _ when propertyType == typeof(global::System.Numerics.Vector4) => "v",
            _ when propertyType == typeof(global::System.Numerics.Matrix4x4) => "mat",
            _ => string.Empty,
        };

        if (typeAnnotation == string.Empty)
        {
            return "m_" + base.GetAttributeName(propertyName, propertyType);
        }

        return "m_" + typeAnnotation + propertyName;
    }
}
