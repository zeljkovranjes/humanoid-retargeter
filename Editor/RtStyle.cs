#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Editor;
using Sandbox;

namespace HumanoidRetargeter.EditorTools;

/// <summary>Shared look of the retargeter's windows: dark rounded cards on the window gray,
/// framed inputs, tinted pills and green accents.</summary>
internal static class RtStyle
{
	public const float ControlHeight = 24f;

	/// <summary>Height of every labelled settings field (inputs and dropdowns alike).</summary>
	public const float FieldHeight = 28f;
	public const float Radius = 4f;

	public static Color ButtonFill => Color.Lerp( Theme.ControlBackground.WithAlpha( 1f ), Color.White, .07f );
	public static Color InputEdge => Color.Lerp( Theme.ControlBackground.WithAlpha( 1f ), Color.White, .17f );

	/// <summary>Inputs sit on the cards' dark gray; a darker fill and a thin edge keep them readable.</summary>
	public static T Framed<T>( T input ) where T : Widget
	{
		input.SetStyles( $"background-color: {Theme.WindowBackground.Hex}; border: 1px solid {InputEdge.Hex}; border-radius: {Radius}px; padding-left: 4px;" );
		input.FixedHeight = ControlHeight;
		return input;
	}

	public static Label Muted( Label label, bool small = false )
	{
		label.SetStyles( small ? $"color: {Theme.TextLight.Hex}; font-size: 11px;" : $"color: {Theme.TextLight.Hex};" );
		return label;
	}

	/// <summary>A labelled settings row: a muted caption of the given width, vertically centered
	/// on the caller's field (see <see cref="Field{T}"/>).</summary>
	public static Layout FieldRow( Widget owner, Layout parent, string caption, float width, string tooltip = null )
	{
		var row = parent.AddRow();
		row.Spacing = 6;
		var label = row.Add( Muted( new Label( caption, owner ) { FixedWidth = width, FixedHeight = FieldHeight, ToolTip = tooltip } ) );
		label.Alignment = TextFlag.LeftCenter;
		return row;
	}

	/// <summary>A framed settings input or dropdown at the shared field height.</summary>
	public static T Field<T>( T input ) where T : Widget
	{
		Framed( input );
		input.FixedHeight = FieldHeight;
		return input;
	}

	public static Color Tone( ChipTone tone ) => tone switch
	{
		ChipTone.Green => Theme.Green,
		ChipTone.Amber => Theme.Yellow,
		_ => Theme.Red,
	};

	public static IconButton Icon( Widget parent, string icon, Action clicked, string tooltip, float size = ControlHeight )
		=> new( icon, clicked, parent )
		{
			FixedSize = size,
			IconSize = 15,
			ToolTip = tooltip,
			Background = ButtonFill,
			Foreground = Theme.Text,
			BackgroundActive = Theme.Green.WithAlpha( .2f ),
			ForegroundActive = Theme.Green,
		};

	/// <summary>Square icon toggle (skeleton, source overlay, ground).</summary>
	public static IconButton Toggle( Widget parent, string icon, bool on, Action<bool> toggled, string tooltip )
	{
		var button = Icon( parent, icon, null, tooltip );
		button.IsToggle = true;
		button.IsActive = on;
		button.OnToggled = toggled;
		return button;
	}

	public static Checkbox Check( Layout parent, string text, bool value, Action<bool> changed, string tooltip = null )
	{
		var box = parent.Add( new Checkbox( text ) { Value = value, ToolTip = tooltip } );
		box.Clicked = () => changed( box.Value );
		return box;
	}

	/// <summary>An engine-unit distance for the read-outs (inches).</summary>
	public static string Inches( float value ) => $"{MathF.Abs( value ):0.0} in";
}
