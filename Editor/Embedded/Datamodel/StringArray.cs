#nullable enable
#pragma warning disable CS3021 // Upstream CLS attributes; host assembly does not claim CLS compliance.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Diagnostics;
using System.Numerics;

namespace HumanoidRetargeter.EditorTools.Embedded.Datamodel;

    public class StringArray : Array<string>
    {
        public StringArray() { }
        public StringArray(IEnumerable<string> enumerable)
            : base(enumerable)
        { }
        public StringArray(int capacity)
            : base(capacity)
        { }
    }
