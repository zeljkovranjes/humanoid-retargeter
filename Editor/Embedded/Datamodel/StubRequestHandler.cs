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
    /// Represents the method that will handle the <see cref="Datamodel.StubRequest"/> event.
    /// </summary>
    /// <param name="id">The Element ID to search for.</param>
    /// <returns>An Element with the requested ID.</returns>
    public delegate Element StubRequestHandler(Guid id);
