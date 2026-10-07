#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Editor;
using Sandbox;

namespace HumanoidRetargeter.EditorTools;

/// <summary>A rounded dark-gray panel with an optional header row (icon, title, then controls).</summary>
internal class RtCard : Widget
{
	public RtCard( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 8;
		Layout.Spacing = 6;
	}

	public Layout Header( string icon, string title, string tooltip = null )
	{
		var row = Layout.AddRow();
		row.Spacing = 6;
		row.Add( new HeaderIcon( this, icon ) );
		var label = row.Add( new Label( title, this ) { ToolTip = tooltip, FixedHeight = RtStyle.ControlHeight } );
		label.SetStyles( "font-weight: 600;" );
		return row;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.SetPen( Theme.ControlBackground.Lighten( .25f ), 1 );
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect.Shrink( 1 ), 6 );
	}

	sealed class HeaderIcon : Widget
	{
		readonly string _icon;

		public HeaderIcon( Widget parent, string icon ) : base( parent )
		{
			_icon = icon;
			FixedSize = 18;
		}

		protected override void OnPaint()
		{
			Paint.SetPen( Theme.Green );
			Paint.DrawIcon( LocalRect, _icon, 16 );
		}
	}
}
