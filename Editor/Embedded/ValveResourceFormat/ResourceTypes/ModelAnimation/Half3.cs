#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.Utils;
using System.Runtime.InteropServices;

namespace HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.ResourceTypes.ModelAnimation;

    [StructLayout(LayoutKind.Sequential, Size = 6)]
    struct Half3(Half x, Half y, Half z)
    {
        public Half X { get; set; } = x;
        public Half Y { get; set; } = y;
        public Half Z { get; set; } = z;


        public static implicit operator Half3(global::System.Numerics.Vector3 v) => new((Half)v.X, (Half)v.Y, (Half)v.Z);
        public static implicit operator global::System.Numerics.Vector3(Half3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

        /// <inheritdoc/>
        /// <remarks>
        /// Returns the vector components formatted as "&lt;X Y Z&gt;" with 3 decimal places.
        /// </remarks>
        public readonly override string ToString() => $"<{X:0.000} {Y:0.000} {Z:0.000}>";
    }
