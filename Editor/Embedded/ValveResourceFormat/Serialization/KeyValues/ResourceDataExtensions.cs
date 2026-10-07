#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.Utils;
using System.Globalization;
using System.Text;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.ResourceTypes;
using KVValueType = HumanoidRetargeter.EditorTools.Embedded.ValveKeyValue.KVValueType;

namespace HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.Serialization.KeyValues;

    /// <summary>
    /// Extension methods for resource data blocks.
    /// </summary>
    public static class ResourceDataExtensions
    {
        /// <summary>
        /// Converts a resource data block to a key-value collection.
        /// </summary>
        public static KVObject AsKeyValueCollection(this Block data) =>
            data switch
            {
                BinaryKV3 kv => kv.Data,
                NTRO ntro => ntro.Output,
                KeyValuesOrNTRO kv => kv.Data,
                _ => throw new InvalidOperationException($"Cannot use {data.GetType().Name} as key-value collection")
            };
    }
