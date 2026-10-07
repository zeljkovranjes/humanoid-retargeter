#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.Utils;
namespace HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat;

    /// <summary>
    /// Render slot types for vertex data.
    /// </summary>
    public enum RenderSlotType
    {
#pragma warning disable CS1591
        RENDER_SLOT_INVALID = -1,
        RENDER_SLOT_PER_VERTEX = 0,
        RENDER_SLOT_PER_INSTANCE = 1
#pragma warning restore CS1591
    }
