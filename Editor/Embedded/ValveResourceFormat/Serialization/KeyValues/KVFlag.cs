#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.Utils;
using KVValueType = HumanoidRetargeter.EditorTools.Embedded.ValveKeyValue.KVValueType;

namespace HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.Serialization.KeyValues;

    /// <summary>
    /// Flags for KeyValue values.
    /// </summary>
    public enum KVFlag : byte
    {
#pragma warning disable CS1591
        None = 0,
        Resource = 1,
        ResourceName = 2,
        Panorama = 3,
        SoundEvent = 4,
        SubClass = 5,
        EntityName = 6,

        // There are more types available in the S2 binaries, but they should not be persisted. Look for "The specific type '%s' cannot be persisted"
        MaxPersistedFlag = EntityName,
#pragma warning restore CS1591
    }
