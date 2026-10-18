using System;
using HumanoidRetargeter.Core.Skeleton;

namespace HumanoidRetargeter.Core.Formats.Dmx;

public static partial class DmxWriter
{
	/// <summary>Returns a stable ASCII flex identifier accepted by ModelDoc's expression parser.</summary>
	public static string MorphName( string name )
	{
		ArgumentNullException.ThrowIfNull( name );
		return "morph" + Convert.ToHexString( System.Text.Encoding.UTF8.GetBytes( name ) ).ToLowerInvariant();
	}

	private static void WriteMorphChannels( Emitter writer, Clip clip )
	{
		var channel = 0;
		foreach ( var (name, values) in clip.MorphWeights )
		{
			if ( values.Length != clip.FrameCount || values.Any( value => !float.IsFinite( value ) ) )
				throw new ArgumentException( $"Morph '{name}' must have one finite weight per frame." );
			writer.BeginArrayElement( "DmeChannel" );
			writer.Attr( "name", "string", name + "_flex_channel" );
			writer.BeginInlineAttr( "toElement", "DmElement" );
			writer.Attr( "name", "string", MorphName( name ) );
			writer.Attr( "flexWeight", "float", "0" );
			writer.EndInlineAttr();
			writer.Attr( "toAttribute", "string", "flexWeight" );
			writer.Attr( "mode", "int", "3" );
			writer.BeginInlineAttr( "log", "DmeFloatLog" );
			writer.BeginArray( "layers" );
			writer.BeginArrayElement( "DmeFloatLogLayer" );
			writer.BeginArray( "times", "time_array" );
			for ( var frame = 0; frame < values.Length; frame++ )
				writer.ArrayValue( Time( frame / (double)clip.Fps ), frame == values.Length - 1 );
			writer.EndArray();
			writer.BeginArray( "values", "float_array" );
			for ( var frame = 0; frame < values.Length; frame++ )
				writer.ArrayValue( F( values[ frame ] ), frame == values.Length - 1 );
			writer.EndArray();
			writer.EndArrayElement( true );
			writer.EndArray();
			writer.EndInlineAttr();
			writer.EndArrayElement( ++channel == clip.MorphWeights.Count );
		}
	}
}
