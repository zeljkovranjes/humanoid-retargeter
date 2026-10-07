#nullable enable
#pragma warning disable CS3021 // Upstream CLS attributes; host assembly does not claim CLS compliance.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Diagnostics;
using System.Numerics;

namespace HumanoidRetargeter.EditorTools.Embedded.Datamodel;

    public class ByteArray : Array<byte>
    {
        public ByteArray() { }
        public ByteArray(IEnumerable<byte> enumerable)
            : base(enumerable)
        { }
        public ByteArray(int capacity)
            : base(capacity)
        { }
    }
