#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Editor;
using Sandbox;

namespace HumanoidRetargeter.EditorTools;

/// <summary>One line of text that is cut short with "…" to the width it is given. A plain label
/// asks for its full text width, so one long line (an error message) widened every row of the
/// clip list past the panel and cut their buttons off.</summary>
public sealed class RtElidedLabel : Widget
{
	string _text = "";
	readonly float _size;
	readonly int _weight;
	Color _color;

	public RtElidedLabel( Widget parent, float size = 9, int weight = 400 ) : base( parent )
	{
		_size = size;
		_weight = weight;
		_color = Theme.Text;
		MinimumWidth = 20;
		FixedHeight = size + 9;
	}

	public string Text
	{
		get => _text;
		set { _text = value ?? ""; Update(); }
	}

	public Color Color
	{
		get => _color;
		set { _color = value; Update(); }
	}

	protected override void OnPaint()
	{
		Paint.SetDefaultFont( _size, _weight );
		Paint.SetPen( _color );
		Paint.DrawText( LocalRect, Paint.GetElidedText( _text, Width, ElideMode.Right, TextFlag.LeftCenter ), TextFlag.LeftCenter );
	}
}
