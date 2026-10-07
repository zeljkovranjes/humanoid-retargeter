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
    /// The exception that is thrown when an error occurs in an <see cref="ICodec"/>.
    /// </summary>
    [Serializable]
    public class CodecException : Exception
    {
        public CodecException(string message)
            : base(message)
        {
        }

        public CodecException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        [SecuritySafeCritical]
        protected CodecException(SerializationInfo info, StreamingContext context)
        {
        }
    }
