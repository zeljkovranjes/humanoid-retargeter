#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace HumanoidRetargeter.EditorTools.Embedded.Datamodel.Codecs;

    /// <summary>
    /// Parameters for reflection based deserialisation
    /// By default it will look for types in the calling assembly (the one which made this class)
    /// </summary>
    /// <param name="attemptReflection">If to use reflection or not.</param>
    /// <param name="additionalTypes">Additional types to consider when matching.</param>
    /// <param name="assembliesToSearch">Additional assemblies to look for types in.</param>
    public class ReflectionParams(bool attemptReflection = true, List<Type>? additionalTypes = null, List<Assembly>? assembliesToSearch = null)
    {
        public bool AttemptReflection = attemptReflection;
        public List<Type> AdditionalTypes = additionalTypes ??= [];
        public List<Assembly> AssembliesToSearch = assembliesToSearch ??= [];
    }
