#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using HumanoidRetargeter.Core.Formats;
using HumanoidRetargeter.Core.Formats.Ant;
using HumanoidRetargeter.Core.Formats.Renderware;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Skeleton;

namespace HumanoidRetargeter.EditorTools;

/// <summary>Profile-chip color classes (visual quality bar: green/amber/red statuses).</summary>
public enum ChipTone
{
	/// <summary>Trusted mapping.</summary>
	Green,

	/// <summary>Auto-mapped / needs review.</summary>
	Amber,

	/// <summary>Unrecognized or failed.</summary>
	Red,
}
