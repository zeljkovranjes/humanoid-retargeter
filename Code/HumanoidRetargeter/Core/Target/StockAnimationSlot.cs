#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;

namespace HumanoidRetargeter.Core.Target;

/// <summary>A supported stock graph slot, independent of the imported clip's format or rig.</summary>
public sealed record StockAnimationSlot(string Id, string Label, bool Looping, string[] Sequences, string Warning)
{
    public string ReplacementPrefix => "hr_replace_" + Id + "_";
}
