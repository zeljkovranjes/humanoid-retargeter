using System;
using System.Linq;
using Editor;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Solve;
using Sandbox;
using SourceClip = HumanoidRetargeter.Core.Skeleton.Clip;
using SourceSkeleton = HumanoidRetargeter.Core.Skeleton.Skeleton;
using VecN = System.Numerics.Vector3;

namespace HumanoidRetargeter.EditorTools;

/// <summary>The source skeleton as the 2D camera sees it, in step with the preview: nearer
/// bones brighter, mapped joints marked. Dragging turns and tilts the camera.</summary>
internal sealed class PlanarSkeletonView : Widget
{
	const float DegreesPerPixel = 0.5f;
	const float Margin = 0.85f;
	const float JointSize = 5f;

	readonly SourceSkeleton _skeleton;
	readonly SourceClip _clip;
	readonly MappingResult _map;
	readonly int _hips;
	readonly float _size;
	(VecN Right, VecN Up, VecN Toward) _camera;
	PlanarView _view;
	int _frame;
	Vector2 _lastMouse;

	/// <summary>The camera was dragged: degrees of turn and of tilt.</summary>
	public Action<float, float> Dragged { get; set; }

	public PlanarSkeletonView( Widget parent, SourceSkeleton skeleton, SourceClip clip, MappingResult map, PlanarView view ) : base( parent )
	{
		_skeleton = skeleton;
		_clip = clip;
		_map = map;
		_hips = map.RoleToBone.TryGetValue( BoneRole.Hips, out var hips ) ? hips : 0;
		_size = FigureSize();
		MinimumSize = new Vector2( 180, 240 );
		MouseTracking = true;
		ToolTip = "The source through the 2D camera. Drag to turn and tilt.";
		SetView( view );
	}

	float FigureSize()
	{
		var world = World( 0 );
		var min = new VecN( float.MaxValue );
		var max = new VecN( float.MinValue );
		foreach ( var bone in world )
		{
			min = VecN.Min( min, bone.Pos );
			max = VecN.Max( max, bone.Pos );
		}
		var size = max - min;
		return MathF.Max( 1f, MathF.Max( size.X, MathF.Max( size.Y, size.Z ) ) );
	}

	HumanoidRetargeter.Core.Maths.XForm[] World( int frame )
		=> new Pose( _clip.Frames[Math.Clamp( frame, 0, _clip.Frames.Count - 1 )] ).ToWorld( _skeleton );

	public void SetView( PlanarView view )
	{
		_view = view;
		_camera = PlanarSolver.SourceCamera( _skeleton, _map, World( 0 ), view );
		Update();
	}

	public void SetFrame( int frame )
	{
		_frame = frame;
		Update();
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();
		Paint.SetBrush( Theme.WindowBackground );
		Paint.DrawRect( LocalRect );

		var world = World( _frame );
		var (right, up, toward) = _camera;
		var hips = world[_hips].Pos;
		var scale = Margin * MathF.Min( LocalRect.Width, LocalRect.Height ) / _size;
		var centre = LocalRect.Center;
		Vector2 Screen( VecN position )
		{
			var offset = position - hips;
			return centre + new Vector2( VecN.Dot( offset, right ), -VecN.Dot( offset, up ) ) * scale;
		}
		float Depth( VecN position ) => Math.Clamp( VecN.Dot( position - hips, toward ) / _size + 0.5f, 0f, 1f );

		var bones = Enumerable.Range( 0, _skeleton.Count )
			.Where( i => _skeleton[i].ParentIndex >= 0 )
			.OrderBy( i => Depth( world[i].Pos ) );
		foreach ( var i in bones )
		{
			Paint.SetPen( Color.Lerp( Theme.TextDisabled, Theme.Yellow, Depth( world[i].Pos ) ), 1.5f );
			Paint.DrawLine( Screen( world[_skeleton[i].ParentIndex].Pos ), Screen( world[i].Pos ) );
		}

		Paint.ClearPen();
		Paint.SetBrush( Theme.Yellow );
		foreach ( var bone in _map.RoleToBone.Values )
			Paint.DrawCircle( Screen( world[bone].Pos ), new Vector2( JointSize ) );
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );
		_lastMouse = e.LocalPosition;
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		base.OnMouseMove( e );
		var delta = e.LocalPosition - _lastMouse;
		_lastMouse = e.LocalPosition;
		if ( (e.ButtonState & MouseButtons.Left) != 0 )
			Dragged?.Invoke( -delta.x * DegreesPerPixel, delta.y * DegreesPerPixel );
	}
}
