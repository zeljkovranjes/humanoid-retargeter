#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;

namespace HumanoidRetargeter.Core.Target;

/// <summary>A supported stock graph slot, independent of the imported clip's format or rig.</summary>
public sealed record StockAnimationSlot(string Id, string Label, bool Looping, string[] Sequences, string Warning)
{
    public string ReplacementPrefix => "hr_replace_" + Id + "_";

    /// <summary>AnimGraph2 clip names (file stems) the slot also takes over: the Human's AnimGraph2 graph names some
    /// gaits differently from its sequences (<c>walk_forward</c> for <c>Walk_N</c>).</summary>
    public string[] Clips { get; init; } = Array.Empty<string>();
}
