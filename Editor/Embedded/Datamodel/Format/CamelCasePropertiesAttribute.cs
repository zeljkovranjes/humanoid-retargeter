#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HumanoidRetargeter.EditorTools.Embedded.Datamodel.Format;

/// <summary>
/// This class' property names are mostly camelCase.
/// </summary>
public class CamelCasePropertiesAttribute : AttributeNamingConventionAttribute
{
    public override string GetAttributeName(string propertyName, Type _)
        => char.ToLowerInvariant(propertyName.AsSpan()[0]) + propertyName[1..];
}
