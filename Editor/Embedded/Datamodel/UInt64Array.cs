#nullable enable
#pragma warning disable CS3021 // Upstream CLS attributes; host assembly does not claim CLS compliance.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Diagnostics;
using System.Numerics;

namespace HumanoidRetargeter.EditorTools.Embedded.Datamodel;

    [CLSCompliant(false)]
    public class UInt64Array : Array<ulong>
    {
        public UInt64Array() { }
        public UInt64Array(IEnumerable<ulong> enumerable)
            : base(enumerable)
        { }
        public UInt64Array(int capacity)
            : base(capacity)
        { }
    }
