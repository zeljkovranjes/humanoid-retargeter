#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using HumanoidRetargeter.Core;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Target;
using HumanoidRetargeter.EditorTools.Embedded.Datamodel;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.IO;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.ResourceTypes;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.EditorTools;

/// <summary>One bone of a Smart Port target: its parent-relative bind transform in engine units (inches, Z up).</summary>
[Alias( "HumanoidRetargeter.Editor.SmartPortBone" )]
public sealed class SmartPortBone
{
	public string Name { get; set; } = "";

	/// <summary>The parent bone's name; null or empty for a root.</summary>
	public string? Parent { get; set; }

	/// <summary>x, y, z.</summary>
	public float[] Position { get; set; } = new float[3];

	/// <summary>x, y, z, w.</summary>
	public float[] Rotation { get; set; } = { 0, 0, 0, 1 };
}
