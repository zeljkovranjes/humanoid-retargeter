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
    /// Defines methods for the encoding and decoding of <see cref="Datamodel"/> objects. Codecs are registered with <see cref="Datamodel.RegisterCodec"/>.
    /// </summary>
    /// <remarks>A new ICodec is instantiated for every encode/decode operation.</remarks>
    /// <seealso cref="CodecUtilities"/>
    public interface ICodec
    {
        /// <summary>
        /// Encodes a <see cref="Datamodel"/> to a <see cref="Stream"/>.
        /// </summary>
        /// <param name="dm">The Datamodel to encode.</param>
        /// <param name="encoding_version">The encoding version to use.</param>
        /// <param name="stream">The output stream.</param>
        void Encode(Datamodel dm, string encoding, int encoding_version, Stream stream);

        /// <summary>
        /// Decodes a <see cref="Datamodel"/> from a <see cref="Stream"/>.
        /// </summary>
        /// <param name="encoding_version">The encoding version that this stream uses.</param>
        /// <param name="format">The format of the Datamodel.</param>
        /// <param name="format_version">The format version of the Datamodel.</param>
        /// <param name="stream">The input stream. Its position will always be 0. Do not dispose.</param>
        /// <param name="defer_mode">The deferred loading mode specified by the caller. Only relevant to implementers of <see cref="IDeferredAttributeCodec"/></param>
        /// <returns></returns>
        Datamodel Decode(string encoding, int encoding_version, string format, int format_version, Stream stream, DeferredMode defer_mode, ReflectionParams reflectionParams);
    }
