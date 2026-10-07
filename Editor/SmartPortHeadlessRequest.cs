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

/// <summary>What <see cref="SmartPortHeadless.Port(SmartPortHeadlessRequest, CancellationToken)"/> works on.</summary>
[Alias( "HumanoidRetargeter.Editor.SmartPortHeadlessRequest" )]
public sealed class SmartPortHeadlessRequest
{
	/// <summary>Asset path of the model whose animation setup is ported (the source), e.g. the stock Citizen.</summary>
	public string SourceModel { get; set; } = "models/citizen/citizen.vmdl";

	/// <summary>Asset path of the source's graph; empty = the graph the compiled source model references.</summary>
	public string? SourceGraph { get; set; }

	/// <summary>
	/// Folders that hold assets by asset path, searched in order: compiled files (<c>&lt;path&gt;_c</c>) for the source
	/// model and what it depends on, editable sources for its graph and subgraphs (e.g. the s&amp;box install's
	/// <c>addons/citizen/Assets</c>).
	/// </summary>
	public string[] ContentRoots { get; set; } = Array.Empty<string>();

	/// <summary>The target's editable ModelDoc source (its meshes, materials, collision...), in engine units.</summary>
	public string TargetVmdl { get; set; } = "";

	/// <summary>The target's skeleton as it compiles (bind pose).</summary>
	public SmartPortBone[] TargetBones { get; set; } = Array.Empty<SmartPortBone>();

	/// <summary>Target bones for humanoid roles the mapper cannot find, by role name (<see cref="HumanoidRetargeter.Core.Mapping.BoneRole"/>).</summary>
	public Dictionary<string, string> TargetRoles { get; set; } = new();

	/// <summary>Asset path the ported model is written to (the graph copy names it as its preview model).</summary>
	public string ModelPath { get; set; } = "";

	/// <summary>Asset folder for the generated data: retargeted clips, the graph and subgraph copies.</summary>
	public string OutputFolder { get; set; } = "";

	/// <summary>The physical folder standing for <see cref="OutputFolder"/>; generated files are written there.</summary>
	public string StagingDirectory { get; set; } = "";

	/// <summary>
	/// ModelDoc list classes whose target entries are kept next to the source's (Smart Port otherwise takes them
	/// from the source), e.g. <c>GameDataList</c>, <c>BoneMarkupList</c>, or <c>AnimationList</c> to keep the
	/// target's own sequences as well. Entries whose name the source already uses are skipped with a warning.
	/// </summary>
	public string[] KeepTargetLists { get; set; } = Array.Empty<string>();

	/// <summary>When set, DMX element ids derive from this seed: the same request writes byte-identical files.</summary>
	public string? Seed { get; set; }
}
