#nullable enable
#pragma warning disable CS3021 // Upstream CLS attributes; host assembly does not claim CLS compliance.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Diagnostics;
using System.Numerics;

namespace HumanoidRetargeter.EditorTools.Embedded.Datamodel;

    public class ColorArray : Array<Color>
    {
        public ColorArray() { }
        public ColorArray(IEnumerable<Color> enumerable)
            : base(enumerable)
        { }
        public ColorArray(int capacity)
            : base(capacity)
        { }
    }
