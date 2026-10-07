#nullable enable
#pragma warning disable CS3021 // Upstream CLS attributes; host assembly does not claim CLS compliance.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Diagnostics;
using System.Numerics;

namespace HumanoidRetargeter.EditorTools.Embedded.Datamodel;

    public class Vector4Array : Array<global::System.Numerics.Vector4>
    {
        public Vector4Array() { }
        public Vector4Array(IEnumerable<global::System.Numerics.Vector4> enumerable)
            : base(enumerable)
        { }
        public Vector4Array(int capacity)
            : base(capacity)
        { }
    }
