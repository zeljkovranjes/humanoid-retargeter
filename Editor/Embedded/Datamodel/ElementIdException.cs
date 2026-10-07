#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeter.EditorTools.Embedded.Datamodel.Codecs;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Security;
using System.Numerics;
using CodecRegistration = System.Tuple<string, int>;
using System.Reflection;

namespace HumanoidRetargeter.EditorTools.Embedded.Datamodel;

    /// <summary>
    /// The exception that is thrown when an <see cref="Element.ID"/> collision occurrs.
    /// </summary>
    [Serializable]
    public class ElementIdException : InvalidOperationException
    {
        internal ElementIdException(string message)
            : base(message)
        {
        }

        [SecuritySafeCritical]
        protected ElementIdException(SerializationInfo info, StreamingContext context)
        {
        }
    }
