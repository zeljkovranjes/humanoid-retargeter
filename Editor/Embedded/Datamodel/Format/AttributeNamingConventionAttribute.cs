#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HumanoidRetargeter.EditorTools.Embedded.Datamodel.Format;

/// <summary>
/// Subclass this attribute to define a custom attribute name convention.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public abstract class AttributeNamingConventionAttribute : System.Attribute
{
    public abstract string GetAttributeName(string propertyName, Type propertyType);
}
