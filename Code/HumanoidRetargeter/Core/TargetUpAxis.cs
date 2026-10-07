#nullable enable annotations

using System;
using System.Collections.Generic;
using HumanoidRetargeter.Core.Cleanup;
using HumanoidRetargeter.Core.Formats;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Solve;
using HumanoidRetargeter.Core.Target;

namespace HumanoidRetargeter.Core;

/// <summary>
/// Axis/unit convention of a <see cref="RetargetTargetSpec"/>'s rig data — drives the DMX
/// axis-system declaration, foot-plant threshold units, and the editor preview's
/// rig-space → engine-space conversion.
/// </summary>
public enum TargetUpAxis
{
    /// <summary>
    /// The s&amp;box source convention: rig authored in centimeters, Y-up (the shipped
    /// citizen rig, FBX targets). The vmdl's ScaleAndMirror 0.3937 + resourcecompiler's
    /// Y-up→Z-up conversion take it to engine space at compile time. Default.
    /// </summary>
    YUpCm,

    /// <summary>
    /// Engine space already: rig read from a compiled model's <c>Model.Bones</c>
    /// (inches, Z-up). The DMX declares a Z-up axis system so the compiler performs no
    /// further axis conversion.
    /// </summary>
    ZUpEngine,

    /// <summary>
    /// A Z-up rig authored in centimeters: FBX targets whose GlobalSettings declare a Z
    /// up-axis (UE and 3ds Max exports; Maya/Blender exports are Y-up). The DMX declares
    /// Z-up (no compile-time rotation — the mesh source is in the same Z-up space) while
    /// the vmdl's ScaleAndMirror 0.3937 still converts cm→inches. Without this, a Z-up
    /// FBX target compiles lying on its back.
    /// </summary>
    ZUpCm,
}
