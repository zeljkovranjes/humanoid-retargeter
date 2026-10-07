#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Editor;
using Sandbox;
using HumanoidRetargeter.Core.Mapping;

namespace HumanoidRetargeter.EditorTools;

/// <summary>
/// Modal preview + confirmation step (design §6 "Preview + preset learning"): shows the
/// retargeted clip on the skinned <see cref="PreviewWidget"/> standing on a ground grid, with
/// play/pause, frame scrubbing and a ground verdict (does the clip touch the floor, float
/// above it or sink into it), then either confirms ("Looks good - Convert", which also offers
/// saving the mapping as a user preset when it came from manual edits or the blind
/// auto-mapper) or cancels. The conversion itself is the caller's job - this dialog only decides.
/// </summary>
public sealed class PreviewDialog : Dialog
{
	readonly PreviewWidget _preview;
	readonly FloatSlider _scrubber;
	readonly Label _frameLabel;
	readonly IconButton _playButton;
	readonly IconButton _ghostButton;
	readonly IconButton _skeletonButton;
	readonly Checkbox _savePresetCheckbox;
	readonly RtPill _groundPill;
	readonly Label _feetLabel;
	readonly ComboBox _cameraCombo;
	readonly List<HumanoidRetargeter.Core.ClipResult> _clips;

	/// <summary>Invoked on confirm with whether "Save as profile for this rig" was checked.</summary>
	public Action<bool> Confirmed { get; set; }

	/// <summary>Invoked when the user cancels (closes without converting).</summary>
	public Action Cancelled { get; set; }

	bool _confirmed;

	static readonly (string Label, float Yaw, float Pitch)[] CameraViews =
	{
		("3/4 view", 35f, 19.3f),
		("Front", 0f, 8f),
		("Side", 90f, 8f),
		("Back", 180f, 8f),
		("Feet", 35f, 2f),
	};

	/// <summary>
	/// Creates the dialog over already-solved clips of one source file.
	/// <paramref name="target"/> supplies the rig + preview model;
	/// <paramref name="mappingSource"/> controls the preset-saving checkbox (shown for
	/// Manual / AutoName / AutoTopology mappings, checked by default).
	/// <paramref name="sourceSkeleton"/>/<paramref name="sourceClip"/>/<paramref name="sourceMapping"/>
	/// (all optional) feed the "Source" stick-skeleton ghost overlay - the toggle is
	/// disabled when they are absent or the mapping cannot anchor the ghost.
	/// </summary>
	public PreviewDialog(
		Widget parent, string fileName, IReadOnlyList<HumanoidRetargeter.Core.ClipResult> clips,
		TargetPickers.ResolvedTarget target, MappingSource mappingSource,
		HumanoidRetargeter.Core.Skeleton.Skeleton sourceSkeleton = null,
		HumanoidRetargeter.Core.Skeleton.Clip sourceClip = null,
		MappingResult sourceMapping = null ) : base( parent )
	{
		_clips = clips.Where( c => c.Success && c.SolvedFrames is { Count: > 0 } ).ToList();

		Window.WindowTitle = $"Preview - {fileName}";
		Window.SetWindowIcon( "preview" );
		Window.SetModal( true, true );
		Window.MinimumWidth = 460;
		Window.MinimumHeight = 460;

		Layout = Layout.Column();
		Layout.Margin = 8;
		Layout.Spacing = 8;

		var card = Layout.Add( new RtCard( this ), 1 );

		// ---- header: clip, camera and view toggles -------------------------------------------
		var header = card.Header( "accessibility_new", fileName );
		header.AddStretchCell();
		if ( _clips.Count > 1 )
		{
			var clipCombo = header.Add( RtStyle.Framed( new ComboBox( card ) { FixedWidth = 150, ToolTip = "Clip" } ) );
			for ( var i = 0; i < _clips.Count; i++ )
			{
				var clip = _clips[i];
				clipCombo.AddItem( clip.ClipName, "movie", () => SelectClip( clip ), selected: i == 0 );
			}
		}
		_cameraCombo = header.Add( RtStyle.Framed( new ComboBox( card ) { FixedWidth = 96,
			ToolTip = "Camera angle. Drag to orbit, right-drag to pan, wheel to zoom, double-click to reset." } ) );

		_preview = new PreviewWidget(
			card, target.Spec.Rig, target.PreviewModelPath, target.PreviewPositionScale,
			target.Spec.UpAxis )
		{
			ShowGround = true,
			SmoothCamera = true,
		};
		_preview.MinimumSize = new Vector2( 240, 240 );
		foreach ( var (label, yaw, pitch) in CameraViews )
			_cameraCombo.AddItem( label, null, () => _preview.SetView( yaw, pitch ), selected: label == "3/4 view" );

		header.Add( RtStyle.Toggle( card, "grid_on", true, on => _preview.ShowGround = on,
			"Ground: floor grid and feet contact rings" ) );
		// Skeleton view: switches between the skinned model and the retargeted animation drawn
		// as bones (and back). Targets with no compiled preview model are locked to it.
		_skeletonButton = header.Add( RtStyle.Toggle( card, "polyline", !_preview.HasModel, on => _preview.SkeletonOnly = on,
			_preview.HasModel ? "Skeleton: show the retargeted animation as bones"
				: "No compiled model exists for this target - the skeleton is the only available view." ) );
		_skeletonButton.Enabled = _preview.HasModel;
		// Source-ghost toggle: a per-preview inspection aid, not a conversion setting.
		_ghostButton = header.Add( RtStyle.Toggle( card, "compare", false, on => _preview.ShowSourceGhost = on,
			"Source: overlay the source clip as a stick skeleton, root-aligned and hip-height-scaled onto the target, synced to the scrub position." ) );
		header.Add( RtStyle.Icon( card, "center_focus_strong", () =>
		{
			_preview.ResetView();
			_cameraCombo.CurrentIndex = 0;
		}, "Reset the camera" ) );

		// ---- viewport ---------------------------------------------------------------------------
		card.Layout.Add( _preview, 1 );

		if ( sourceSkeleton is not null && sourceClip is not null && sourceMapping is not null )
			_preview.SetSourceGhost( sourceSkeleton, sourceClip, sourceMapping );
		_ghostButton.Enabled = _preview.HasSourceGhost;

		if ( !_preview.HasModel )
		{
			card.Layout.Add( RtStyle.Muted( new Label( this )
			{
				Text = "No compiled preview model exists for this target - showing the retargeted animation as a skeleton.",
				WordWrap = true,
			}, small: true ) );
		}

		// ---- transport --------------------------------------------------------------------------
		var transport = card.Layout.AddRow();
		transport.Spacing = 8;
		_playButton = transport.Add( RtStyle.Icon( card, "pause", TogglePlay, "Play / pause" ) );
		_scrubber = transport.Add( new FloatSlider( card ) { FixedHeight = RtStyle.ControlHeight }, 1 );
		_scrubber.Minimum = 0;
		_scrubber.OnValueEdited = () =>
		{
			_preview.Scrub( (int)_scrubber.Value );
			_playButton.Icon = "play_arrow";
			UpdateReadouts();
		};
		_frameLabel = transport.Add( RtStyle.Muted( new Label( this ) { Text = "0 / 0", FixedWidth = 70, FixedHeight = RtStyle.ControlHeight } ) );
		_frameLabel.Alignment = TextFlag.RightCenter;

		// ---- ground: does this clip touch the floor? -------------------------------------------
		var ground = card.Layout.AddRow();
		ground.Spacing = 8;
		_groundPill = ground.Add( new RtPill( card, "", Theme.TextLight ) );
		_feetLabel = ground.Add( RtStyle.Muted( new Label( this ) { FixedHeight = RtStyle.ControlHeight }, small: true ), 1 );

		_preview.FrameChanged = frame =>
		{
			_scrubber.Value = frame;
			UpdateReadouts();
		};

		// ---- confirm row ------------------------------------------------------------------------
		var confirm = Layout.AddRow();
		confirm.Spacing = 8;

		if ( mappingSource is MappingSource.Manual or MappingSource.AutoName or MappingSource.AutoTopology )
		{
			_savePresetCheckbox = confirm.Add( new Checkbox( "Save as profile for this rig" ) { Value = true } );
			_savePresetCheckbox.ToolTip =
				"Stores the confirmed mapping as a user preset (keyed by the skeleton's signature), "
				+ "so this rig is recognized automatically next time.";
		}

		confirm.AddStretchCell();
		confirm.Add( new RtButton( this, "Cancel", null, Close, "Close without converting", 28 ) );

		var ok = confirm.Add( new Button.Primary( "Looks good - Convert" ) { Icon = "check", Tint = Theme.Green, FixedHeight = 28 } );
		ok.Enabled = _clips.Count > 0;
		ok.Clicked = () =>
		{
			_confirmed = true;
			Confirmed?.Invoke( _savePresetCheckbox?.Value ?? false );
			Close();
		};

		if ( _clips.Count > 0 )
			SelectClip( _clips[0] );

		Window.Size = new Vector2( 560, 620 );
	}

	void SelectClip( HumanoidRetargeter.Core.ClipResult clip )
	{
		_preview.SetClip( clip );
		_preview.Playing = true;
		_playButton.Icon = "pause";
		_scrubber.Maximum = Math.Max( _preview.FrameCount - 1, 0 );
		_scrubber.Value = 0;
		UpdateGroundVerdict();
		UpdateReadouts();
	}

	void TogglePlay()
	{
		_preview.Playing = !_preview.Playing;
		_playButton.Icon = _preview.Playing ? "pause" : "play_arrow";
	}

	/// <summary>Frame counter and the live feet read-out.</summary>
	void UpdateReadouts()
	{
		_frameLabel.Text = $"{_preview.CurrentFrame + 1} / {_preview.FrameCount}";
		if ( _preview.FootClearance is not { } feet )
		{
			_feetLabel.Text = "";
			return;
		}
		var tolerance = _preview.FrameContactTolerance;
		_feetLabel.Text = feet > tolerance ? $"This frame: feet {RtStyle.Inches( feet )} above the ground"
			: feet < -tolerance ? $"This frame: feet {RtStyle.Inches( feet )} below the ground"
			: "This frame: feet on the ground";
	}

	/// <summary>Whole-clip verdict: does the character ever touch the floor, or sink into it?</summary>
	void UpdateGroundVerdict()
	{
		if ( _preview.ClipClearance is not { } lowest )
		{
			_groundPill.Set( "", Theme.TextLight );
			return;
		}
		var tolerance = _preview.ContactTolerance;
		if ( lowest > tolerance )
			_groundPill.Set( $"FLOATS {RtStyle.Inches( lowest ).ToUpperInvariant()}", Theme.Yellow,
				$"The feet never reach the ground in this clip: the lowest they get is {RtStyle.Inches( lowest )} above where they stand at rest. "
				+ "Jumps are expected to leave the ground; a walk or idle that floats points at the source's ground placement." );
		else if ( lowest < -tolerance )
			_groundPill.Set( $"SINKS {RtStyle.Inches( lowest ).ToUpperInvariant()}", Theme.Red,
				$"The feet go {RtStyle.Inches( lowest )} below where they stand at rest at some point in this clip." );
		else
			_groundPill.Set( "GROUNDED", Theme.Green, "The feet reach the ground in this clip without sinking through it." );
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();
		if ( !_confirmed )
			Cancelled?.Invoke();
	}
}
