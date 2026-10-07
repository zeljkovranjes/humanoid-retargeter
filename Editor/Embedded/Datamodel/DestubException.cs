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
    /// The exception that is thrown when an error occurs while destubbing an attribute value.
    /// </summary>
    [Serializable]
    public class DestubException : Exception
    {
        internal DestubException(Attribute attr, Exception innerException)
            : base("An exception occured while destubbing the value of an attribute.", innerException)
        {
            Data.Add("Element", ((Element?)attr.Owner)?.ID);
            Data.Add("Attribute", attr.Name);
        }

        internal DestubException(ElementArray array, int index, Exception innerException)
            : base("An exception occured while destubbing an array item.", innerException)
        {
            var arrayOwner = array.Owner;
            if(arrayOwner is not null)
            {
                Data.Add("Element", ((Element)arrayOwner).ID);
            }
            else
            {
                Data.Add("Element", null);
            }
            Data.Add("Index", index);
        }

        [SecuritySafeCritical]
        protected DestubException(SerializationInfo info, StreamingContext context)
        {
        }
    }
