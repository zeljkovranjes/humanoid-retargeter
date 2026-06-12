using System;
using System.Collections.Generic;
using System.IO;
using HumanoidRetargeter.Formats.Bvh;
using HumanoidRetargeter.Formats.Fbx;
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

	/// <summary>Row status (drives row color + icon).</summary>
	public EntryStatus Status { get; set; }

	/// <summary>One-line detail shown for failures / conversion results.</summary>
	public string StatusDetail { get; set; } = "";

	/// <summary>Per-clip results of the last conversion of this file, if any.</summary>
	public List<HumanoidRetargeter.ClipResult> LastClips { get; } = new();

	/// <summary>Number of animation takes in the file (0 when unreadable).</summary>
	public int ClipCount => Scene?.Clips.Count ?? 0;

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
			entry.Scene = Import( entry.Bytes, filePath );
			entry.Signature = SkeletonSignature.Compute( entry.Scene.Skeleton );
		}
		catch ( Exception e )
		{
			entry.Status = EntryStatus.Failed;
			entry.StatusDetail = e.Message;
			return entry;
		}

		ResolveMapping( entry, assetsPath );
		return entry;
	}

	/// <summary>Extension-based import with content sniffing as fallback (mirrors the
	/// facade's import rules - the facade's own importer entry point is internal).</summary>
	static SourceScene Import( byte[] bytes, string filePath )
	{
		var ext = Path.GetExtension( filePath ).TrimStart( '.' ).ToLowerInvariant();
		switch ( ext )
		{
			case "fbx": return FbxImporter.Import( bytes );
			case "bvh": return BvhImporter.Import( bytes );
		}

		// Unknown extension: sniff. BVH is ASCII starting with HIERARCHY; try FBX otherwise.
		var head = System.Text.Encoding.UTF8.GetString( bytes, 0, Math.Min( bytes.Length, 256 ) );
		if ( head.TrimStart().StartsWith( "HIERARCHY", StringComparison.OrdinalIgnoreCase ) )
			return BvhImporter.Import( bytes );
		return FbxImporter.Import( bytes );
	}

	static void ResolveMapping( SourceFileEntry entry, string assetsPath )
	{
		var skeleton = entry.Scene.Skeleton;

		if ( assetsPath is not null
			&& UserPresets.TryLoad( assetsPath, entry.Signature, skeleton ) is { } userMap )
		{
			entry.Mapping = userMap;
			entry.MappingConfirmed = true;
			entry.Status = EntryStatus.Ready;
			return;
		}

		if ( ProfileDetector.Detect( skeleton ) is { } detected )
		{
			entry.Mapping = detected.Result;
			entry.Status = EntryStatus.Ready;
			return;
		}

		entry.Mapping = AutoMapper.Map( skeleton );
		entry.NeedsUserDecision = entry.Mapping.Confidence < ProfileDetector.DetectionThreshold;
		entry.Status = EntryStatus.NeedsReview;
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
