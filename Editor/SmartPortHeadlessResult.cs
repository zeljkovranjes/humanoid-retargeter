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

/// <summary>A finished headless Smart Port.</summary>
[Alias( "HumanoidRetargeter.Editor.SmartPortHeadlessResult" )]
public sealed class SmartPortHeadlessResult
{
	/// <summary>The ported model's ModelDoc source: target geometry, source animation setup, the graph copy assigned.</summary>
	public string Vmdl { get; set; } = "";

	/// <summary>Asset path of the graph copy the model uses.</summary>
	public string GraphPath { get; set; } = "";

	/// <summary>Asset paths of the generated files the model and its graphs use (all under the output folder).</summary>
	public string[] Files { get; set; } = Array.Empty<string>();

	/// <summary>The ported model's sequences.</summary>
	public string[] Sequences { get; set; } = Array.Empty<string>();

	/// <summary>Source bone name to the target bone that took its place (unmatched source helpers keep their name).</summary>
	public Dictionary<string, string> BoneMap { get; set; } = new();

	/// <summary>Target to source leg length: how translations and root motion were scaled.</summary>
	public float MotionScale { get; set; } = 1;

	/// <summary>False when the two armatures matched and the clips were copied as they are.</summary>
	public bool Retargeted { get; set; }

	/// <summary>Everything that was not ported exactly.</summary>
	public string[] Warnings { get; set; } = Array.Empty<string>();
}
