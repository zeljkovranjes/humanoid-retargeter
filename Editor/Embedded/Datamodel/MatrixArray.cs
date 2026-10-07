#nullable enable
#pragma warning disable CS3021 // Upstream CLS attributes; host assembly does not claim CLS compliance.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Diagnostics;
using System.Numerics;

namespace HumanoidRetargeter.EditorTools.Embedded.Datamodel;

    public class MatrixArray : Array<global::System.Numerics.Matrix4x4>
    {
        public MatrixArray() { }
        public MatrixArray(IEnumerable<global::System.Numerics.Matrix4x4> enumerable)
            : base(enumerable)
        { }
        public MatrixArray(int capacity)
            : base(capacity)
        { }
    }
