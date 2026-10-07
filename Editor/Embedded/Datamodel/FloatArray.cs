#nullable enable
#pragma warning disable CS3021 // Upstream CLS attributes; host assembly does not claim CLS compliance.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Diagnostics;
using System.Numerics;

namespace HumanoidRetargeter.EditorTools.Embedded.Datamodel;

    public class FloatArray : Array<float>
    {
        public FloatArray() { }
        public FloatArray(IEnumerable<float> enumerable)
            : base(enumerable)
        { }
        public FloatArray(int capacity)
            : base(capacity)
        { }
    }
