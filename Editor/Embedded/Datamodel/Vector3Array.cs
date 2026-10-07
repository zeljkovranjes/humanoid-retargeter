#nullable enable
#pragma warning disable CS3021 // Upstream CLS attributes; host assembly does not claim CLS compliance.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Diagnostics;
using System.Numerics;

namespace HumanoidRetargeter.EditorTools.Embedded.Datamodel;

    public class Vector3Array : Array<global::System.Numerics.Vector3>
    {
        public Vector3Array() { }
        public Vector3Array(IEnumerable<global::System.Numerics.Vector3> enumerable)
            : base(enumerable)
        { }
        public Vector3Array(int capacity)
            : base(capacity)
        { }
    }
