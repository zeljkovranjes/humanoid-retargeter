#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Editor;
using Sandbox;

namespace HumanoidRetargeter.EditorTools;

/// <summary>A rounded tinted label: profile and confidence, ground verdicts, counts.</summary>
internal sealed class RtPill : Widget
{
	string _text = "";
	Color _color = Theme.TextLight;

	public RtPill( Widget parent, string text, Color color, string tooltip = null ) : base( parent )
	{
		FixedHeight = 18;
		ToolTip = tooltip;
		Set( text, color );
	}

	public void Set( string text, Color color, string tooltip = null )
	{
		text ??= "";
		if ( tooltip is not null )
			ToolTip = tooltip;
		_text = text;
		_color = color;
		FixedWidth = 6.6f * text.Length + 16;
		Visible = text.Length > 0;
		Update();
	}

	protected override void OnPaint()
	{
		if ( string.IsNullOrEmpty( _text ) )
			return;
		Paint.Antialiasing = true;
		Paint.ClearPen();
		Paint.SetBrush( _color.WithAlpha( 0.18f ) );
		Paint.DrawRect( LocalRect, LocalRect.Height * 0.5f );
		Paint.SetPen( _color );
		Paint.SetDefaultFont( 7, 600 );
		Paint.DrawText( LocalRect, _text );
	}
}
