#nullable enable
#pragma warning disable CS3021 // Upstream CLS attributes; host assembly does not claim CLS compliance.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Diagnostics;
using System.Numerics;

namespace HumanoidRetargeter.EditorTools.Embedded.Datamodel;

    public class Vector2Array : Array<global::System.Numerics.Vector2>
    {
        public Vector2Array() { }
        public Vector2Array(IEnumerable<global::System.Numerics.Vector2> enumerable)
            : base(enumerable)
        { }
        public Vector2Array(int capacity)
            : base(capacity)
        { }
    }
