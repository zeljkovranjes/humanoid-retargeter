#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HumanoidRetargeter.EditorTools.Embedded.Datamodel.Format;

/// <summary>
/// This class' property names are mostly lowercase.
/// </summary>
public class LowercasePropertiesAttribute : AttributeNamingConventionAttribute
{
    public override string GetAttributeName(string propertyName, Type _)
        => propertyName.ToLower();
}
