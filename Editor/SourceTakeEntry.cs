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

/// <summary>
/// One animation take of a loaded source file = one row in the retarget window. A file with
/// N takes unpacks into N of these; they share the owning <see cref="SourceFileEntry"/>'s
/// bytes/skeleton/mapping (mapping is per FILE — one skeleton per file) but carry their own
/// conversion lifecycle, are previewed/converted via a per-take facade request
/// (<see cref="HumanoidRetargeter.Core.RetargetRequest.TakeIndex"/>) and are removable one by one.
/// </summary>
public sealed class SourceTakeEntry
{
	internal SourceTakeEntry( SourceFileEntry file, int takeIndex, string takeName )
	{
		File = file;
		TakeIndex = takeIndex;
		TakeName = string.IsNullOrWhiteSpace( takeName ) ? $"take {takeIndex + 1}" : takeName;
	}

	/// <summary>Owning file entry (bytes, scene, mapping, file-level status).</summary>
	public SourceFileEntry File { get; }

	/// <summary>0-based take index (what <see cref="HumanoidRetargeter.Core.RetargetRequest.TakeIndex"/> accepts).</summary>
	public int TakeIndex { get; }

	/// <summary>Take (clip) name from the source file.</summary>
	public string TakeName { get; }

	/// <summary>Conversion lifecycle of THIS take (Converting/Converted/Failed); null while
	/// no conversion ran — the row then shows the file's mapping status.</summary>
	public EntryStatus? ConversionStatus { get; set; }

	/// <summary>One-line detail of the last conversion of this take.</summary>
	public string StatusDetail { get; set; } = "";

	/// <summary>Per-clip results of the last conversion of this take, if any.</summary>
	public List<HumanoidRetargeter.Core.ClipResult> LastClips { get; } = new();

	/// <summary>Identity handed to the facade as <c>SourceId</c> and used to join
	/// <see cref="HumanoidRetargeter.Core.ClipResult"/>s back to this row (full path + take
	/// index: file names AND take names may collide across the session).</summary>
	public string SourceId => $"{File.FilePath}::take:{TakeIndex}";

	/// <summary>Row label. Files with several animations list the ANIMATIONS, not the
	/// container: each row shows the actual clip name (e.g. <c>Defense0</c>), with the file
	/// it came from relegated to the row tooltip. Single-animation files keep the file name
	/// (their take name is often exporter junk like <c>mixamo.com</c>).</summary>
	public string DisplayName => File.Takes.Count > 1 ? TakeName : File.FileName;

	internal Core.Target.LocomotionSuggestion SuggestLocomotion()
	{
		// External definitions are sub-ranges of a timeline; do not infer their travel
		// from the entire take. Their explicit names are still useful suggestions.
		var clip = File.ClipDefinitions is null && TakeIndex < File.Scene.Clips.Count ? File.Scene.Clips[TakeIndex] : null;
		var name = File.ClipDefinitions is not null ? TakeName : Path.GetFileNameWithoutExtension( DisplayName );
		return Core.Target.LocomotionSuggestion.Detect( name, File.Scene.Skeleton, File.Mapping, clip );
	}

	/// <summary>What the row shows: the take's conversion status when one ran, else the
	/// file's mapping status.</summary>
	public EntryStatus EffectiveStatus => ConversionStatus ?? File.Status;
}
