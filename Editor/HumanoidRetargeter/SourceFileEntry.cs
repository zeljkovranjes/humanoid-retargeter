using System;
using System.Collections.Generic;
using System.IO;
using HumanoidRetargeter.Formats;
using HumanoidRetargeter.Mapping;
using HumanoidRetargeter.Skeleton;

namespace HumanoidRetargeter.Editor;

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

/// <summary>
/// One source animation file added to the retarget window: bytes, imported scene,
/// skeleton signature, and the CURRENT effective mapping. Every entry carries its own
/// mapping (per-item profiles - a session may mix Mixamo + ActorCore + BVH files);
/// the mapping is always passed to the facade as <c>MappingOverride</c> so the
/// conversion uses exactly what the row shows (and what any preview showed).
/// </summary>
public sealed class SourceFileEntry
{
	/// <summary>Absolute path of the source file.</summary>
	public string FilePath { get; private set; }

	/// <summary>File name (no directory).</summary>
	public string FileName => Path.GetFileName( FilePath );

	/// <summary>Raw file bytes (what gets handed to the facade).</summary>
	public byte[] Bytes { get; private set; }

	/// <summary>Imported scene; null when the file was unreadable.</summary>
	public SourceScene Scene { get; private set; }

	/// <summary><see cref="SkeletonSignature"/> of the source skeleton (user preset key).</summary>
	public string Signature { get; private set; }

	/// <summary>Current effective mapping (preset / user preset / auto / manual).</summary>
	public MappingResult Mapping { get; set; }

	/// <summary>True when no preset matched and the auto map is below the detection
	/// threshold - drives the "No known profile found" dialog.</summary>
	public bool NeedsUserDecision { get; set; }

	/// <summary>Set when the user accepted the mapping (auto-map dialog choice, manual
	/// apply, or preview confirmation).</summary>
	public bool MappingConfirmed { get; set; }

	/// <summary>Retarget this file with the experimental deep-learning solver instead of
	/// the geometric one (chosen in the no-profile dialog; design §6 option 2). The entry's
	/// mapping is still carried for the report/heuristics, but the DL solver ignores
	/// per-role assignments. Applies to all of this file's takes.</summary>
	public bool UseDlSolver { get; set; }

	/// <summary>File-level (mapping lifecycle) status: Ready / NeedsReview / Failed-to-load.
	/// Take rows overlay their own conversion status (<see cref="SourceTakeEntry"/>).</summary>
	public EntryStatus Status { get; set; }

	/// <summary>One-line detail shown for load failures.</summary>
	public string StatusDetail { get; set; } = "";

	/// <summary>
	/// One entry per animation take in the file, in take-index order (empty when the file is
	/// unreadable or has no animation). The window shows one row per take: a multi-take file
	/// unpacks into individual entries that share THIS file's bytes/skeleton/mapping but are
	/// previewed, converted and removed independently (one facade request per take).
	/// When <see cref="ClipDefinitions"/> is set the rows are one per DEFINITION instead.
	/// </summary>
	public List<SourceTakeEntry> Takes { get; } = new();

	/// <summary>
	/// External clip definitions parsed from a Unity <c>&lt;FilePath&gt;.meta</c> sidecar
	/// (ModelImporter → clipAnimations); null when the file has no parseable sidecar. Unity
	/// animation packs ship FBX files whose clips are sub-ranges of ONE timeline — with
	/// definitions present the file unpacks into one row per definition (like multi-take
	/// files do per take), and each row's facade request carries the definitions plus its
	/// index so conversion AND preview slice the resampled take to the definition's range.
	/// </summary>
	public IReadOnlyList<ExternalClipDef> ClipDefinitions { get; private set; }

	/// <summary>Number of convertible clips in the file: Unity sidecar definitions when
	/// present, else the animation takes (0 when unreadable).</summary>
	public int ClipCount => Scene is null ? 0 : ClipDefinitions?.Count ?? Scene.Clips.Count;

	SourceFileEntry() { }

	/// <summary>
	/// Loads + inspects a source file, resolving its mapping in the same order the facade
	/// uses, with the Editor-only user-preset lookup in front: user preset (by skeleton
	/// signature) → shipped preset detection → best-effort auto map (flagged
	/// <see cref="NeedsUserDecision"/> below the detection threshold). Never throws: an
	/// unreadable file yields a <see cref="EntryStatus.Failed"/> entry.
	/// </summary>
	public static SourceFileEntry Load( string filePath, string assetsPath )
	{
		var entry = new SourceFileEntry { FilePath = filePath };
		try
		{
			entry.Bytes = File.ReadAllBytes( filePath );
			entry.Scene = Retargeter.ImportSource( entry.Bytes, filePath );
			entry.Signature = SkeletonSignature.Compute( entry.Scene.Skeleton );
			for ( var i = 0; i < entry.Scene.Clips.Count; i++ )
				entry.Takes.Add( new SourceTakeEntry( entry, i, entry.Scene.Clips[i].Name ) );
			LoadUnityClipDefinitions( entry );
		}
		catch ( Exception e )
		{
			entry.Status = EntryStatus.Failed;
			entry.StatusDetail = e.Message;
			return entry;
		}

		// Readable but animation-less: nothing to convert, say so up front (take rows are
		// what conversion operates on, so a takeless entry would otherwise sit inert).
		if ( entry.Takes.Count == 0 )
		{
			entry.Status = EntryStatus.Failed;
			entry.StatusDetail = "Source file contains no animation takes.";
			return entry;
		}

		ResolveMapping( entry, assetsPath );
		return entry;
	}

	/// <summary>
	/// Unity-sidecar support: animation packs ship FBX files whose clips are sub-ranges of
	/// ONE timeline, defined in <c>&lt;FilePath&gt;.meta</c> (ModelImporter → clipAnimations).
	/// When such a sidecar parses to at least one definition, the take rows are replaced by
	/// one row per DEFINITION (named like in Unity); <see cref="RetargetWindow"/> then passes
	/// the definitions + the row index on every facade request, which slices the resampled
	/// take accordingly (preview included — it re-solves through the same request). Never
	/// fails the entry: a missing/unreadable/garbage sidecar keeps the plain take rows.
	/// </summary>
	static void LoadUnityClipDefinitions( SourceFileEntry entry )
	{
		try
		{
			var metaPath = entry.FilePath + ".meta";
			if ( !File.Exists( metaPath ) )
			{
				// Unity packs store the animation list ONLY in the sidecar. A single long
				// take with no sidecar is the classic symptom of an FBX copied out of its
				// pack without the .meta — tell the user what to do instead of silently
				// showing one opaque clip.
				if ( entry.Scene.Clips.Count == 1 && entry.Scene.Clips[0].FrameCount > 400 )
					entry.StatusDetail = "Single continuous timeline. If this file comes from a Unity "
						+ "animation pack, place its '" + entry.FileName + ".meta' next to it and "
						+ "re-add - the individual animations are defined there.";
				return;
			}
			var defs = UnityMeta.ParseClipAnimations( File.ReadAllText( metaPath ) );
			if ( defs.Count == 0 )
				return;

			entry.ClipDefinitions = defs;
			entry.Takes.Clear();
			for ( var i = 0; i < defs.Count; i++ )
				entry.Takes.Add( new SourceTakeEntry( entry, i, defs[i].Name ) );
		}
		catch ( Exception )
		{
			// Optional enhancement only - reading the sidecar must never break the file row.
		}
	}

	/// <summary>The facade's single mapping cascade with the Editor-side user-preset lookup
	/// hooked in front of preset detection (the facade itself can do no file IO).</summary>
	static void ResolveMapping( SourceFileEntry entry, string assetsPath )
	{
		var skeleton = entry.Scene.Skeleton;
		var (map, report) = Retargeter.ResolveMapping(
			skeleton,
			mappingOverride: null,
			userPresetLookup: assetsPath is null
				? null
				: signature => UserPresets.TryLoad( assetsPath, signature, skeleton ) );

		entry.Mapping = map;
		switch ( map.Source )
		{
			case MappingSource.UserPreset:
				entry.MappingConfirmed = true;
				entry.Status = EntryStatus.Ready;
				break;
			case MappingSource.Preset:
				entry.Status = EntryStatus.Ready;
				break;
			default:
				entry.NeedsUserDecision = report.NeedsUserDecision;
				entry.Status = EntryStatus.NeedsReview;
				break;
		}
	}

	/// <summary>Display string for the profile chip, e.g. <c>"mixamo · 100%"</c>.</summary>
	public string ChipText
		=> Mapping is null ? "unreadable" : $"{Mapping.ProfileName} · {Mapping.Confidence * 100f:0}%";

	/// <summary>Chip color class: green = trusted (preset / user preset / confirmed ≥ threshold),
	/// amber = auto-mapped / needs review, red = unreadable or failed.</summary>
	public ChipTone Tone
	{
		get
		{
			if ( Mapping is null || Status == EntryStatus.Failed )
				return ChipTone.Red;
			if ( Mapping.Source is MappingSource.Preset or MappingSource.UserPreset )
				return ChipTone.Green;
			if ( MappingConfirmed && Mapping.Confidence >= ProfileDetector.DetectionThreshold )
				return ChipTone.Green;
			return ChipTone.Amber;
		}
	}
}

/// <summary>
/// One animation take of a loaded source file = one row in the retarget window. A file with
/// N takes unpacks into N of these; they share the owning <see cref="SourceFileEntry"/>'s
/// bytes/skeleton/mapping (mapping is per FILE — one skeleton per file) but carry their own
/// conversion lifecycle, are previewed/converted via a per-take facade request
/// (<see cref="HumanoidRetargeter.RetargetRequest.TakeIndex"/>) and are removable one by one.
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

	/// <summary>0-based take index (what <see cref="HumanoidRetargeter.RetargetRequest.TakeIndex"/> accepts).</summary>
	public int TakeIndex { get; }

	/// <summary>Take (clip) name from the source file.</summary>
	public string TakeName { get; }

	/// <summary>Conversion lifecycle of THIS take (Converting/Converted/Failed); null while
	/// no conversion ran — the row then shows the file's mapping status.</summary>
	public EntryStatus? ConversionStatus { get; set; }

	/// <summary>One-line detail of the last conversion of this take.</summary>
	public string StatusDetail { get; set; } = "";

	/// <summary>Per-clip results of the last conversion of this take, if any.</summary>
	public List<HumanoidRetargeter.ClipResult> LastClips { get; } = new();

	/// <summary>Identity handed to the facade as <c>SourceId</c> and used to join
	/// <see cref="HumanoidRetargeter.ClipResult"/>s back to this row (full path + take
	/// index: file names AND take names may collide across the session).</summary>
	public string SourceId => $"{File.FilePath}::take:{TakeIndex}";

	/// <summary>Row label. Files with several animations list the ANIMATIONS, not the
	/// container: each row shows the actual clip name (e.g. <c>Defense0</c>), with the file
	/// it came from relegated to the row tooltip. Single-animation files keep the file name
	/// (their take name is often exporter junk like <c>mixamo.com</c>).</summary>
	public string DisplayName => File.Takes.Count > 1 ? TakeName : File.FileName;

	/// <summary>What the row shows: the take's conversion status when one ran, else the
	/// file's mapping status.</summary>
	public EntryStatus EffectiveStatus => ConversionStatus ?? File.Status;
}

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
