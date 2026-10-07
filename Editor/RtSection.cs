#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Editor;
using Sandbox;

namespace HumanoidRetargeter.EditorTools;

/// <summary>Small uppercase caption with a hairline, separating groups inside a card.</summary>
internal sealed class RtSection : Widget
{
	readonly string _text;

	public RtSection( Widget parent, string text ) : base( parent )
	{
		_text = text.ToUpperInvariant();
		FixedHeight = 18;
	}

	protected override void OnPaint()
	{
		Paint.SetDefaultFont( 7, 600 );
		Paint.SetPen( Theme.TextLight );
		var size = Paint.MeasureText( _text );
		Paint.DrawText( new Rect( 0, 0, size.x + 2, Height ), _text, TextFlag.LeftCenter );
		Paint.SetPen( Theme.ControlBackground.Lighten( .45f ), 1 );
		Paint.DrawLine( new Vector2( size.x + 10, Height * .5f ), new Vector2( Width, Height * .5f ) );
	}
}
