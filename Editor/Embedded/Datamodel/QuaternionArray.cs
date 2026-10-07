#nullable enable
#pragma warning disable CS3021 // Upstream CLS attributes; host assembly does not claim CLS compliance.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Diagnostics;
using System.Numerics;

namespace HumanoidRetargeter.EditorTools.Embedded.Datamodel;

    public class QuaternionArray : Array<global::System.Numerics.Quaternion>
    {
        public QuaternionArray() { }
        public QuaternionArray(IEnumerable<global::System.Numerics.Quaternion> enumerable)
            : base(enumerable)
        { }
        public QuaternionArray(int capacity)
            : base(capacity)
        { }
    }
