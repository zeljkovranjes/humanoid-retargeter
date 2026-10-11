#nullable enable annotations

using System;
using System.Collections.Generic;
using Editor;
using Sandbox;
using HumanoidRetargeter.Core;
using HumanoidRetargeter.Core.Trim;

namespace HumanoidRetargeter.EditorTools;

/// <summary>
/// Trim editor for one take: the solved clip on the preview character above a timeline. Select frames and
/// delete them or keep only them, trim before or after the playhead, undo (Ctrl+Z), redo (Ctrl+Y) and
/// restore the whole clip (Ctrl+R). Apply hands the kept frames back; converting is the caller's job.
/// </summary>
public sealed class TrimDialog : Dialog
{
	readonly ClipResult _clip;
	readonly HumanoidRetargeter.Core.Skeleton.Skeleton _skeleton;
	readonly System.Numerics.Vector3 _up;
	readonly TrimEdit _edit;
	readonly PreviewWidget _preview;
	readonly TrimTimeline _timeline;
	readonly IconButton _playButton;
	readonly Label _readout;
	readonly IconButton _keep, _delete, _undo, _redo, _restore;

	/// <summary>Invoked on Apply with the kept frames, or null when the whole clip is kept.</summary>
	public Action<IReadOnlyList<int>> Applied { get; set; }

	/// <summary>Opens the editor over <paramref name="clip"/> (the untrimmed solve), starting from <paramref name="kept"/>.</summary>
	public TrimDialog( Widget parent, string name, ClipResult clip, TargetPickers.ResolvedTarget target, IReadOnlyList<int> kept ) : base( parent )
	{
		_clip = clip;
		_skeleton = target.Spec.Rig.Skeleton;
		_up = target.Spec.UpAxis == TargetUpAxis.ZUpEngine ? System.Numerics.Vector3.UnitZ : System.Numerics.Vector3.UnitY;
		_edit = new TrimEdit( clip.SolvedFrames.Count, kept );

		Window.WindowTitle = $"Trim - {name}";
		Window.SetWindowIcon( "content_cut" );
		Window.SetModal( true, true );
		Window.MinimumWidth = 520;
		Window.MinimumHeight = 520;

		Layout = Layout.Column();
		Layout.Margin = 8;
		Layout.Spacing = 8;

		var card = Layout.Add( new RtCard( this ), 1 );
		var header = card.Header( "content_cut", name );
		header.AddStretchCell();
		_preview = new PreviewWidget( card, target.Spec.Rig, target.PreviewModelPath, target.PreviewPositionScale, target.Spec.UpAxis )
		{
			ShowGround = true,
			SmoothCamera = true,
		};
		_preview.MinimumSize = new Vector2( 240, 240 );
		header.Add( RtStyle.Icon( card, "center_focus_strong", _preview.ResetView, "Reset the camera" ) );
		card.Layout.Add( _preview, 1 );

		// ---- transport and edits ---------------------------------------------------------------
		var transport = card.Layout.AddRow();
		transport.Spacing = 4;
		transport.Add( RtStyle.Icon( card, "first_page", () => Seek( 0 ), "First frame (Home)" ) );
		transport.Add( RtStyle.Icon( card, "chevron_left", () => Seek( _timeline.Playhead - 1 ), "Previous frame (Left)" ) );
		_playButton = transport.Add( RtStyle.Icon( card, "pause", TogglePlay, "Play / pause (Space)" ) );
		transport.Add( RtStyle.Icon( card, "chevron_right", () => Seek( _timeline.Playhead + 1 ), "Next frame (Right)" ) );
		transport.Add( RtStyle.Icon( card, "last_page", () => Seek( _edit.Kept.Count - 1 ), "Last frame (End)" ) );
		_readout = transport.Add( RtStyle.Muted( new Label( this ) { FixedHeight = RtStyle.ControlHeight } ), 1 );
		_keep = transport.Add( RtStyle.Icon( card, "crop", () => Run( () => _edit.Keep( Sel.Start, Sel.End ) ), "Keep only the selected frames (K)" ) );
		_delete = transport.Add( RtStyle.Icon( card, "content_cut", () => Run( () => _edit.Delete( Sel.Start, Sel.End ) ), "Cut the selected frames out (Delete)" ) );
		_undo = transport.Add( RtStyle.Icon( card, "undo", () => Run( _edit.Undo ), "Undo (Ctrl+Z)" ) );
		_redo = transport.Add( RtStyle.Icon( card, "redo", () => Run( _edit.Redo ), "Redo (Ctrl+Y)" ) );
		_restore = transport.Add( RtStyle.Icon( card, "restore", () => Run( _edit.Restore ), "Restore the whole animation (Ctrl+R)" ) );

		_timeline = card.Layout.Add( new TrimTimeline( card, _edit, clip.Fps )
		{
			Seeked = frame => _preview.Scrub( frame ),
			BuildContextMenu = BuildMenu,
		} );
		_timeline.SelectionChanged = Refresh;
		_preview.FrameChanged = frame =>
		{
			_timeline.Playhead = frame;
			_timeline.Update();
			Refresh();
		};
		card.Layout.Add( RtStyle.Muted( new Label( this )
		{
			Text = "Drag on the timeline to select frames. Delete cuts them out, K keeps only them, right-click trims before or after a frame. "
				+ "Ctrl+Z undo, Ctrl+Y redo, Ctrl+R restore. Wheel zooms.",
			WordWrap = true,
		}, small: true ) );

		// ---- confirm ----------------------------------------------------------------------------
		var confirm = Layout.AddRow();
		confirm.Spacing = 8;
		confirm.AddStretchCell();
		confirm.Add( new RtButton( this, "Cancel", null, Close, "Close without changing the trim", 28 ) );
		var apply = confirm.Add( new Button.Primary( "Apply trim" ) { Icon = "check", Tint = Theme.Green, FixedHeight = 28 } );
		apply.Clicked = () =>
		{
			Applied?.Invoke( _edit.IsTrimmed ? _edit.Kept : null );
			Close();
		};

		ShowTrim();
		_preview.Playing = true;
		Window.Size = new Vector2( 640, 680 );
	}

	(int Start, int End) Sel => _timeline.Selection ?? (_timeline.Playhead, _timeline.Playhead);

	void BuildMenu( Menu menu, int frame )
	{
		if ( _timeline.Selection is { } s )
		{
			menu.AddOption( "Cut selection out", "content_cut", () => Run( () => _edit.Delete( s.Start, s.End ) ) );
			menu.AddOption( "Keep only selection", "crop", () => Run( () => _edit.Keep( s.Start, s.End ) ) );
			menu.AddSeparator();
		}
		menu.AddOption( $"Trim everything before frame {frame}", "first_page", () => Run( () => _edit.Keep( frame, _edit.Kept.Count - 1 ) ) );
		menu.AddOption( $"Trim everything after frame {frame}", "last_page", () => Run( () => _edit.Keep( 0, frame ) ) );
		menu.AddSeparator();
		menu.AddOption( "Restore the whole animation", "restore", () => Run( _edit.Restore ) );
	}

	/// <summary>Runs one edit; a refused edit (it would leave fewer than two frames) changes nothing.</summary>
	void Run( Func<bool> edit )
	{
		if ( edit() )
			ShowTrim();
	}

	/// <summary>Puts the kept frames on the preview, keeping the playhead and play state.</summary>
	void ShowTrim()
	{
		var playing = _preview.Playing;
		var frames = ClipTrim.Apply( _clip.SolvedFrames, _edit.Kept, _skeleton, _up );
		_preview.SetClip( _clip.WithFrames( frames ) );
		_timeline.Refit();
		_preview.Scrub( _timeline.Playhead );
		_preview.Playing = playing;
		_playButton.Icon = playing ? "pause" : "play_arrow";
		Refresh();
	}

	void Seek( int frame )
	{
		frame = Math.Clamp( frame, 0, _edit.Kept.Count - 1 );
		_timeline.Playhead = frame;
		_timeline.Update();
		_preview.Scrub( frame );
		_playButton.Icon = "play_arrow";
		Refresh();
	}

	void TogglePlay()
	{
		_preview.Playing = !_preview.Playing;
		_playButton.Icon = _preview.Playing ? "pause" : "play_arrow";
	}

	void Refresh()
	{
		var fps = Math.Max( _clip.Fps, 1f );
		var count = _edit.Kept.Count;
		var text = $"Frame {_timeline.Playhead} / {count - 1}  ·  {(count - 1) / fps:0.0#} s";
		if ( _edit.IsTrimmed )
			text += $"  ·  {_edit.FrameCount - count} of {_edit.FrameCount} cut";
		if ( _timeline.Selection is { } s )
			text += $"  ·  {s.End - s.Start + 1} selected";
		_readout.Text = text;
		_keep.Enabled = _delete.Enabled = _timeline.Selection is not null;
		_undo.Enabled = _edit.CanUndo;
		_redo.Enabled = _edit.CanRedo;
		_restore.Enabled = _edit.IsTrimmed;
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		base.OnKeyPress( e );
		var handled = true;
		switch ( e.Key )
		{
			case KeyCode.Space: TogglePlay(); break;
			case KeyCode.Left: Seek( _timeline.Playhead - 1 ); break;
			case KeyCode.Right: Seek( _timeline.Playhead + 1 ); break;
			case KeyCode.Home: Seek( 0 ); break;
			case KeyCode.End: Seek( _edit.Kept.Count - 1 ); break;
			case KeyCode.Delete or KeyCode.Backspace when _timeline.Selection is { } s: Run( () => _edit.Delete( s.Start, s.End ) ); break;
			case KeyCode.K when _timeline.Selection is { } s: Run( () => _edit.Keep( s.Start, s.End ) ); break;
			case KeyCode.Z when e.HasCtrl && e.HasShift: Run( _edit.Redo ); break;
			case KeyCode.Z when e.HasCtrl: Run( _edit.Undo ); break;
			case KeyCode.Y when e.HasCtrl: Run( _edit.Redo ); break;
			case KeyCode.R when e.HasCtrl: Run( _edit.Restore ); break;
			case KeyCode.Escape when _timeline.Selection is not null: _timeline.Select( null, null ); break;
			default: handled = false; break;
		}
		if ( handled )
			e.Accepted = true;
	}
}
