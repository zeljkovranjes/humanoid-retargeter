#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Editor;
using Sandbox;

namespace HumanoidRetargeter.EditorTools;

/// <summary>The empty state of the clip list: a prompt and the add button. Accepts animation
/// files from the OS and from the asset browser.</summary>
internal sealed class RtDropZone : Widget
{
	readonly Action<IReadOnlyList<string>> _add;
	int _hover;

	public RtDropZone( Widget parent, Action<IReadOnlyList<string>> add, Action choose ) : base( parent )
	{
		_add = add;
		AcceptDrops = true;
		Layout = Layout.Column();
		Layout.Margin = 12;
		Layout.Spacing = 6;
		Layout.AddStretchCell();
		var title = Layout.Add( new Label( "Drop animation files here", this ) { Alignment = TextFlag.Center } );
		title.SetStyles( "font-weight: 600;" );
		Layout.Add( RtStyle.Muted( new Label( "FBX · BVH · GLB · glTF · VRM · ANM · AN5 · CBA — or right-click them in the Asset Browser", this )
			{ Alignment = TextFlag.Center, WordWrap = true }, small: true ) );
		var row = Layout.AddRow();
		row.AddStretchCell();
		row.Add( new Button.Primary( "Add Files…" ) { Icon = "add", Tint = Theme.Green, FixedHeight = 28, Clicked = choose } );
		row.AddStretchCell();
		Layout.AddStretchCell();
	}

	public override void OnDragHover( DragEvent e )
	{
		var valid = RtDrop.Paths( e.Data ).Count > 0;
		_hover = valid ? 1 : -1;
		if ( valid )
			e.Action = DropAction.Link;
		Update();
	}

	public override void OnDragDrop( DragEvent e )
	{
		_hover = 0;
		var paths = RtDrop.Paths( e.Data );
		if ( paths.Count > 0 )
		{
			e.Action = DropAction.Link;
			_add( paths );
		}
		Update();
	}

	public override void OnDragLeave()
	{
		_hover = 0;
		Update();
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.SetPen( _hover == 1 ? Theme.Green : _hover < 0 ? Theme.Red : Theme.ControlBackground.Lighten( .45f ), _hover == 0 ? 1 : 2 );
		Paint.SetBrush( _hover == 1 ? Theme.Green.WithAlpha( .06f ) : Theme.WindowBackground.WithAlpha( .5f ) );
		Paint.DrawRect( LocalRect.Shrink( 1 ), 6 );
	}
}
