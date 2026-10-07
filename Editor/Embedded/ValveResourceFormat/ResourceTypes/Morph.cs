// Minimal extraction-only adapters for the vendored VRF model exporter (MIT).
#nullable enable
using System;
using System.Collections.Generic;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.ResourceTypes;

namespace HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.ResourceTypes;

    // Model/animation export only needs controller definitions from MRPH. The upstream
    // DMX mesh exporter does not export vertex morph deltas. Keep source targets when possible.
    public sealed class Morph(BlockType type) : KeyValuesOrNTRO(type, "MorphSetData_t")
    {
        public ModelFlex.FlexController[] FlexControllers => Array.Empty<ModelFlex.FlexController>();
    }
