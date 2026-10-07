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

    static class Extensions
    {
        public static Type MakeListType(this Type t)
        {
            return typeof(List<>).MakeGenericType(t);
        }
    }
