#nullable enable annotations

using System;
using Editor;
using Sandbox;
using HumanoidRetargeter.Core.Trim;

namespace HumanoidRetargeter.EditorTools;

/// <summary>
/// The trim editor's timeline (same look as the text-to-animation timeline): a ruler, the kept frames as
/// one track with a red marker wherever a section was cut out, the selection with drag handles and the
/// playhead. Click scrubs, drag selects, Shift+click extends, edges resize, the wheel zooms, right-click
/// opens the cut menu.
/// </summary>
internal sealed class TrimTimeline : Widget
{
	const float RulerHeight = 22f;
	const float Side = 10f;

	readonly TrimEdit _edit;
	readonly float _fps;
	float _zoom = 1f;  // 1 = whole clip fits
	float _scroll;     // first visible frame
	enum Drag { None, Scrub, Pending, Range }
	Drag _drag;
	int _dragStart;
	float _pressX;

	/// <summary>Playhead position in the kept frames.</summary>
	public int Playhead { get; set; }

	/// <summary>Selected positions (inclusive), or null.</summary>
	public (int Start, int End)? Selection { get; private set; }

	/// <summary>The user moved the playhead.</summary>
	public Action<int> Seeked { get; set; }

	/// <summary>The selection changed.</summary>
	public Action SelectionChanged { get; set; }

	/// <summary>Fills the right-click menu for the frame under the cursor.</summary>
	public Action<Menu, int> BuildContextMenu { get; set; }

	public TrimTimeline( Widget parent, TrimEdit edit, float fps ) : base( parent )
	{
		_edit = edit;
		_fps = Math.Max( fps, 1f );
		FixedHeight = 64;
		MouseTracking = true;
		FocusMode = FocusMode.Click;
	}

	int Count => Math.Max( 1, _edit.Kept.Count );
	float TrackWidth => Math.Max( 10f, Width - Side * 2 );
	float VisibleFrames => Math.Max( 4f, (Count - 1) / _zoom );
	float PixelsPerFrame => TrackWidth / Math.Max( 1f, VisibleFrames );
	float FrameToX( float frame ) => Side + (frame - _scroll) * PixelsPerFrame;
	float XToFrame( float x ) => _scroll + (x - Side) / PixelsPerFrame;
	int Snap( float x ) => Math.Clamp( (int)MathF.Round( XToFrame( x ) ), 0, Count - 1 );

	/// <summary>Sets or clears the selection (null when start equals end).</summary>
	public void Select( int? start, int? end )
	{
		Selection = start is { } s && end is { } e && s != e
			? (Math.Clamp( Math.Min( s, e ), 0, Count - 1 ), Math.Clamp( Math.Max( s, e ), 0, Count - 1 ))
			: null;
		SelectionChanged?.Invoke();
		Update();
	}

	/// <summary>Called after the kept frames changed: clamps the view, playhead and selection.</summary>
	public void Refit()
	{
		_zoom = Math.Clamp( _zoom, 1f, Math.Max( 1f, Count / 8f ) );
		_scroll = Math.Clamp( _scroll, 0, Math.Max( 0, Count - 1 - VisibleFrames ) );
		Playhead = Math.Clamp( Playhead, 0, Count - 1 );
		Select( null, null );
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();
		Paint.SetBrush( Theme.WindowBackground );
		Paint.DrawRect( LocalRect, 4 );

		var accent = Theme.Green;
		var top = RulerHeight + 4;
		var bottom = Height - 6;

		// kept frames
		Paint.SetBrush( accent.WithAlpha( .18f ) );
		Paint.DrawRect( new Rect( FrameToX( 0 ), top, FrameToX( Count - 1 ) - FrameToX( 0 ), bottom - top ), 3 );

		// selection
		if ( Selection is { } range )
		{
			var x0 = FrameToX( range.Start );
			var x1 = FrameToX( range.End );
			Paint.SetBrush( accent.WithAlpha( .3f ) );
			Paint.DrawRect( new Rect( x0, RulerHeight, Math.Max( 2, x1 - x0 ), Height - RulerHeight ) );
			foreach ( var x in new[] { x0, x1 } )
			{
				Paint.SetPen( accent.Lighten( .25f ), 2 );
				Paint.DrawLine( new Vector2( x, RulerHeight ), new Vector2( x, Height ) );
				Paint.ClearPen();
				Paint.SetBrush( accent.Lighten( .25f ) );
				Paint.DrawRect( new Rect( x - 3, Height - 16, 6, 12 ), 2 ); // grab handle
			}
		}

		// cuts: where removed frames used to be
		for ( var i = 0; i + 1 < _edit.Kept.Count; i++ )
		{
			if ( !_edit.IsCutAfter( i ) )
				continue;
			var x = FrameToX( i + .5f );
			if ( x < 0 || x > Width )
				continue;
			Paint.SetPen( Theme.Red, 2 );
			Paint.DrawLine( new Vector2( x, top - 2 ), new Vector2( x, bottom + 2 ) );
			Paint.ClearPen();
			Paint.SetBrush( Theme.Red );
			Paint.DrawPolygon( new[] { new Vector2( x - 4, top - 4 ), new Vector2( x + 4, top - 4 ), new Vector2( x, top + 2 ) } );
		}

		// ruler: labels about 60px apart, never under the playhead badge or off the right edge
		Paint.SetDefaultFont( 7 );
		var step = NiceStep( 60f / PixelsPerFrame );
		var badge = FrameToX( Playhead );
		for ( var f = (int)MathF.Floor( _scroll / step ) * step; f <= Math.Min( Count - 1, _scroll + VisibleFrames + step ); f += step )
		{
			var x = FrameToX( f );
			if ( f < 0 || x < Side - 1 || x > Width )
				continue;
			Paint.SetPen( Color.White.WithAlpha( .12f ), 1 );
			Paint.DrawLine( new Vector2( x, RulerHeight - 6 ), new Vector2( x, Height ) );
			var label = $"{f}  {f / _fps:0.0#}s";
			var w = Paint.MeasureText( label ).x;
			if ( x + 3 + w > Width - 4 || (x + 3 + w > badge - 16 && x + 3 < badge + 16) )
				continue;
			Paint.SetPen( Theme.TextLight );
			Paint.DrawText( new Rect( x + 3, 2, w + 2, RulerHeight - 6 ), label, TextFlag.LeftCenter );
		}

		// playhead
		Paint.SetPen( accent, 2 );
		Paint.DrawLine( new Vector2( badge, 0 ), new Vector2( badge, Height ) );
		Paint.ClearPen();
		Paint.SetBrush( accent );
		Paint.DrawRect( new Rect( badge - 14, 1, 28, 14 ), 3 );
		Paint.SetPen( Color.White );
		Paint.SetDefaultFont( 7, 600 );
		Paint.DrawText( new Rect( badge - 14, 1, 28, 14 ), Playhead.ToString(), TextFlag.Center );

		Paint.SetPen( Theme.ControlBackground.Lighten( .4f ), 1 );
		Paint.ClearBrush();
		Paint.DrawRect( LocalRect.Shrink( .5f ), 4 );
	}

	static int NiceStep( float framesPerLabel )
	{
		foreach ( var s in new[] { 1, 2, 5, 10, 15, 30, 60, 120, 300, 600 } )
			if ( s >= framesPerLabel )
				return s;
		return 1200;
	}

	void Seek( int frame )
	{
		Playhead = frame;
		Seeked?.Invoke( frame );
		Update();
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );
		var x = e.LocalPosition.x;
		var f = Snap( x );
		if ( e.RightMouseButton )
		{
			var menu = new Menu( this );
			BuildContextMenu?.Invoke( menu, f );
			menu.OpenAtCursor();
			return;
		}
		if ( !e.LeftMouseButton )
			return;
		e.Accepted = true;

		// grab an edge of the selection to resize it
		if ( Selection is { } r )
		{
			if ( MathF.Abs( x - FrameToX( r.Start ) ) < 6 ) { _drag = Drag.Range; _dragStart = r.End; return; }
			if ( MathF.Abs( x - FrameToX( r.End ) ) < 6 ) { _drag = Drag.Range; _dragStart = r.Start; return; }
		}
		if ( e.HasShift )
		{
			_drag = Drag.Range;
			_dragStart = Playhead;
			Select( Playhead, f );
			return;
		}
		if ( e.LocalPosition.y > RulerHeight )
		{
			// on the track: a click scrubs, a drag selects
			_drag = Drag.Pending;
			_dragStart = f;
			_pressX = x;
			Seek( f );
			return;
		}
		_drag = Drag.Scrub;
		Seek( f );
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		base.OnMouseMove( e );
		var x = e.LocalPosition.x;
		var f = Snap( x );
		if ( _drag == Drag.None )
			Cursor = Selection is { } r && (MathF.Abs( x - FrameToX( r.Start ) ) < 6 || MathF.Abs( x - FrameToX( r.End ) ) < 6)
				? CursorShape.SizeH : CursorShape.Arrow;
		switch ( _drag )
		{
			case Drag.Scrub: Seek( f ); break;
			case Drag.Pending when MathF.Abs( x - _pressX ) > 4:
				_drag = Drag.Range;
				Select( _dragStart, f );
				break;
			case Drag.Range: Select( _dragStart, f ); break;
		}
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		base.OnMouseReleased( e );
		if ( _drag == Drag.Pending )
			Select( null, null ); // a plain click clears the selection
		_drag = Drag.None;
	}

	protected override void OnWheel( WheelEvent e )
	{
		base.OnWheel( e );
		var anchor = XToFrame( e.Position.x );
		_zoom = Math.Clamp( _zoom * (e.Delta > 0 ? 1.15f : 1f / 1.15f), 1f, Math.Max( 1f, Count / 8f ) );
		_scroll = Math.Clamp( anchor - (e.Position.x - Side) / PixelsPerFrame, 0, Math.Max( 0, Count - 1 - VisibleFrames ) );
		Update();
		e.Accept();
	}
}
