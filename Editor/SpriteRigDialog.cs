using System;
using System.Linq;
using Editor;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Sprites;
using Sandbox;
using VecN = System.Numerics.Vector2;

namespace HumanoidRetargeter.EditorTools;

/// <summary>Shows the skeleton read off a sprite before it is rigged: how it was drawn, and
/// every joint, which can be dragged to where it really is.</summary>
internal sealed class SpriteRigDialog : Dialog
{
	readonly SpriteTarget.Image _image;
	readonly Canvas _canvas;
	SpriteRig _rig;

	/// <summary>The skeleton was accepted.</summary>
	public Action<SpriteRig> Confirmed { get; set; }

	public SpriteRigDialog( Widget parent, SpriteTarget.Image image ) : base( parent )
	{
		_image = image;
		_rig = SpriteRig.Fit( image.Alpha, image.Width, image.Height );

		Window.WindowTitle = $"2D sprite - {image.Name}";
		Window.SetWindowIcon( "image" );
		Window.SetModal( true, true );
		Window.MinimumWidth = 460;
		Window.MinimumHeight = 560;

		Layout = Layout.Column();
		Layout.Margin = 8;
		Layout.Spacing = 8;

		var card = Layout.Add( new RtCard( this ), 1 );
		var header = card.Header( "accessibility_new", "Place the skeleton" );
		header.AddStretchCell();
		var drawn = header.Add( RtStyle.Framed( new ComboBox( card ) { FixedWidth = 150, ToolTip = "How the sprite is drawn" } ) );
		drawn.AddItem( "Front", null, () => Refit( false, null ), selected: !_rig.IsProfile );
		drawn.AddItem( "Profile, facing right", null, () => Refit( true, false ), selected: _rig.IsProfile && !_rig.FacesLeft );
		drawn.AddItem( "Profile, facing left", null, () => Refit( true, true ), selected: _rig.IsProfile && _rig.FacesLeft );
		_canvas = card.Layout.Add( new Canvas( card, image, () => _rig ), 1 );

		var buttons = Layout.AddRow();
		buttons.Spacing = 6;
		buttons.AddStretchCell();
		buttons.Add( new Button( "Cancel" ) { Clicked = Close } );
		buttons.Add( new Button.Primary( "Use this sprite" )
		{
			Clicked = () =>
			{
				Close();
				Confirmed?.Invoke( _rig );
			},
		} );

		Window.Size = new Vector2( 520, 640 );
	}

	void Refit( bool isProfile, bool? facesLeft )
	{
		_rig = SpriteRig.Fit( _image.Alpha, _image.Width, _image.Height, isProfile, facesLeft );
		_canvas?.Update();
	}

	sealed class Canvas : Widget
	{
		const float JointRadius = 5f;
		const float GrabRadius = 12f;

		static readonly (BoneRole From, BoneRole To)[] Lines =
		{
			(BoneRole.Hips, BoneRole.Spine0), (BoneRole.Spine0, BoneRole.Spine1), (BoneRole.Spine1, BoneRole.Spine2),
			(BoneRole.Spine2, BoneRole.Neck), (BoneRole.Neck, BoneRole.Head),
			(BoneRole.Spine2, BoneRole.UpperArmL), (BoneRole.UpperArmL, BoneRole.LowerArmL), (BoneRole.LowerArmL, BoneRole.HandL),
			(BoneRole.Spine2, BoneRole.UpperArmR), (BoneRole.UpperArmR, BoneRole.LowerArmR), (BoneRole.LowerArmR, BoneRole.HandR),
			(BoneRole.Hips, BoneRole.UpperLegL), (BoneRole.UpperLegL, BoneRole.LowerLegL), (BoneRole.LowerLegL, BoneRole.FootL), (BoneRole.FootL, BoneRole.ToeL),
			(BoneRole.Hips, BoneRole.UpperLegR), (BoneRole.UpperLegR, BoneRole.LowerLegR), (BoneRole.LowerLegR, BoneRole.FootR), (BoneRole.FootR, BoneRole.ToeR),
		};

		readonly SpriteTarget.Image _image;
		readonly Func<SpriteRig> _rig;
		readonly Pixmap _pixmap;
		BoneRole? _dragged;

		public Canvas( Widget parent, SpriteTarget.Image image, Func<SpriteRig> rig ) : base( parent )
		{
			_image = image;
			_rig = rig;
			var bitmap = Bitmap.CreateFromBytes( image.Png );
			_pixmap = Pixmap.FromBitmap( bitmap );
			MinimumSize = new Vector2( 240, 320 );
			MouseTracking = true;
		}

		float Scale => MathF.Min( LocalRect.Width / _image.Width, LocalRect.Height / _image.Height );
		Vector2 Offset => (LocalRect.Size - new Vector2( _image.Width, _image.Height ) * Scale) * 0.5f;
		Vector2 ToScreen( VecN pixel ) => new Vector2( pixel.X, pixel.Y ) * Scale + Offset;

		protected override void OnPaint()
		{
			Paint.Antialiasing = true;
			Paint.Draw( new Rect( Offset, new Vector2( _image.Width, _image.Height ) * Scale ), _pixmap );
			var rig = _rig();
			Paint.SetPen( Theme.Blue, 2f );
			foreach ( var (from, to) in Lines )
				Paint.DrawLine( ToScreen( rig.Joints[from] ), ToScreen( rig.Joints[to] ) );
			Paint.ClearPen();
			foreach ( var role in rig.EditableJoints )
			{
				Paint.SetBrush( role == _dragged ? Theme.Yellow : Theme.Green );
				Paint.DrawCircle( ToScreen( rig.Joints[role] ), JointRadius * 2f );
			}
		}

		protected override void OnMousePress( MouseEvent e )
		{
			base.OnMousePress( e );
			var rig = _rig();
			var mouse = e.LocalPosition;
			var nearest = rig.EditableJoints
				.OrderBy( role => ToScreen( rig.Joints[role] ).Distance( mouse ) )
				.First();
			if ( ToScreen( rig.Joints[nearest] ).Distance( mouse ) <= GrabRadius )
				_dragged = nearest;
			Update();
		}

		protected override void OnMouseMove( MouseEvent e )
		{
			base.OnMouseMove( e );
			if ( _dragged is not { } role )
				return;
			var pixel = (e.LocalPosition - Offset) / Scale;
			_rig().Move( role, new VecN( pixel.x, pixel.y ) );
			Update();
		}

		protected override void OnMouseReleased( MouseEvent e )
		{
			base.OnMouseReleased( e );
			_dragged = null;
			Update();
		}
	}
}
