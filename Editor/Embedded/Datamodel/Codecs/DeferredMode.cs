#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace HumanoidRetargeter.EditorTools.Embedded.Datamodel.Codecs;

    /// <summary>
    /// Values which instruct <see cref="IDeferredAttributeCodec"/> implementers on how to use deferred Attribute reading.
    /// </summary>
    public enum DeferredMode
    {
        /// <summary>
        /// The codec decides whether to defer attribute loading.
        /// </summary>
        Automatic,
        /// <summary>
        /// The codec loads all attributes immediately.
        /// </summary>
        Disabled
    }
