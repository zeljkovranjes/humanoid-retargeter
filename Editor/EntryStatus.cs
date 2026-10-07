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

/// <summary>Lifecycle state of one source file row in the retarget window.</summary>
public enum EntryStatus
{
	/// <summary>Mapped via a preset / user preset / confirmed mapping - ready to convert (green).</summary>
	Ready,

	/// <summary>Auto-mapped below the detection threshold or otherwise awaiting user review (amber).</summary>
	NeedsReview,

	/// <summary>Conversion in flight.</summary>
	Converting,

	/// <summary>Last conversion succeeded (green).</summary>
	Converted,

	/// <summary>Unreadable file or failed conversion (red).</summary>
	Failed,
}
