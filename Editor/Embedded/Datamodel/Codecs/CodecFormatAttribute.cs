#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace HumanoidRetargeter.EditorTools.Embedded.Datamodel.Codecs;

    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
    public sealed class CodecFormatAttribute : System.Attribute
    {
        /// <summary>
        /// Specifies a Datamodel encoding name and some versions that a class handles.
        /// </summary>
        /// <param name="name">The encoding name that the codec handles.</param>
        /// <param name="versions">The encoding version(s) that the codec handles.</param>
        public CodecFormatAttribute(string name, params int[] versions)
        {
            Name = name;
            Versions = versions;
        }

        /// <summary>
        /// Specifies a Datamodel encoding name and version that a class handles.
        /// </summary>
        /// <remarks>This constructor is CLS-compliant.</remarks>
        /// <param name="name">The encoding name that the codec handles.</param>
        /// <param name="version">An encoding version that the codec handles.</param>
        public CodecFormatAttribute(string name, int version)
        {
            Name = name;
            Versions = [version];
        }

        public string Name { get; private set; }
        public int[] Versions { get; private set; }
    }
