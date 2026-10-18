using System.Globalization;
using System.Numerics;
using System.Text;

namespace HumanoidRetargeter.Core.Formats.Mmd;

using Vector3 = System.Numerics.Vector3;

internal static class MmdMotionReader
{
	internal static MmdMotion ReadVmd( byte[] data )
	{
		var reader = new MmdReader( data );
		var signature = reader.FixedText( 30, Encoding.ASCII );
		if ( signature != "Vocaloid Motion Data 0002" && signature != "Vocaloid Motion Data file" )
			throw new FormatException( "Invalid VMD signature." );
		reader.Skip( signature.EndsWith( "0002", StringComparison.Ordinal ) ? 20 : 10 );
		var motion = new MmdMotion();
		var count = reader.Count( 111, 1_000_000 );
		for ( var i = 0; i < count; i++ )
		{
			var name = reader.JapaneseText( 15 );
			var frame = ReadFrame( reader );
			var position = reader.Vector();
			var rotation = reader.Rotation();
			var interpolation = reader.Bytes( 64 );
			for ( var j = 0; j < 16; j++ )
			{
				if ( interpolation[ j ] > 127 )
					throw new FormatException( "Invalid VMD interpolation control point." );
			}
			motion.Add( name, new MmdKeyframe( frame, position, rotation, interpolation ) );
		}
		// Older exporters end after any of these sections; a partial section is still an error.
		if ( reader.Remaining > 0 )
		{
			var morphCount = reader.Count( 23 );
			for ( var i = 0; i < morphCount; i++ )
			{
				var name = reader.JapaneseText( 15 );
				var frame = ReadFrame( reader );
				var weight = reader.Float();
				if ( !motion.Morphs.TryGetValue( name, out var keys ) )
				{
					keys = new List<(int, float)>();
					motion.Morphs.Add( name, keys );
				}
				keys.Add( (frame, weight) );
			}
		}
		foreach ( var size in new[] { 61, 28, 9 } )
		{
			if ( reader.Remaining == 0 )
				break;
			var sectionCount = reader.Count( size );
			reader.Skip( sectionCount * size );
			if ( sectionCount > 0 )
					motion.Notes.Add( "VMD camera, light or shadow tracks were ignored; character motion is retargeted." );
		}
		if ( reader.Remaining > 0 )
		{
			var states = reader.Count( 9 );
			for ( var i = 0; i < states; i++ )
			{
				var frame = ReadFrame( reader );
				reader.Byte(); // model visibility
				var ikCount = reader.Count( 21 );
				for ( var j = 0; j < ikCount; j++ )
				{
					var name = reader.JapaneseText( 20 );
					if ( !motion.IkStates.TryGetValue( name, out var keys ) )
					{
						keys = new List<(int, bool)>();
						motion.IkStates.Add( name, keys );
					}
					keys.Add( (frame, reader.Byte() != 0) );
				}
			}
		}
		motion.Sort();
		return motion;
	}

	private static int ReadFrame( MmdReader reader )
	{
		var frame = reader.Int();
		if ( frame < 0 || frame > 108000 )
			throw new FormatException( "VMD frame lies outside the supported one-hour timeline." );
		return frame;
	}

	internal static MmdMotion ReadVpd( byte[] data )
	{
		var utf8 = data.Length >= 3 && data[ 0 ] == 0xef && data[ 1 ] == 0xbb && data[ 2 ] == 0xbf;
		var text = (utf8 ? Encoding.UTF8.GetString( data ) : MmdJapaneseEncoding.Decode( data )).TrimStart( '\ufeff' );
		var lines = text.Replace( "\r", "" ).Split( '\n' )
			.Select( line => line.Split( new[] { "//" }, StringSplitOptions.None )[ 0 ].Trim() )
			.Where( line => line.Length > 0 ).ToArray();
		if ( lines.Length < 3 || lines[ 0 ] != "Vocaloid Pose Data file"
			|| !int.TryParse( lines[ 2 ].TrimEnd( ';' ), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count )
			|| count < 0 || count > 4096 )
			throw new FormatException( "Invalid VPD header or bone count." );
		var motion = new MmdMotion();
		var readBones = 0;
		for ( var i = 3; i < lines.Length; )
		{
			var header = lines[ i++ ];
			var brace = header.IndexOf( '{' );
			if ( brace < 0 )
				throw new FormatException( "Invalid VPD bone block." );
			var name = header[ (brace + 1).. ].Trim();
			if ( header.StartsWith( "Morph", StringComparison.Ordinal ) )
			{
				if ( i + 1 >= lines.Length || lines[ i + 1 ] != "}" )
					throw new FormatException( "Truncated VPD morph block." );
				var weight = Numbers( lines[ i ], 1 )[ 0 ];
				motion.Morphs[ name ] = new List<(int, float)> { (0, weight) };
				i += 2;
				continue;
			}
			if ( !header.StartsWith( "Bone", StringComparison.Ordinal ) || name.Length == 0
				|| i + 2 >= lines.Length || lines[ i + 2 ] != "}" || motion.Tracks.ContainsKey( name ) )
				throw new FormatException( "Invalid or duplicate VPD bone block." );
			var position = Numbers( lines[ i++ ], 3 );
			var rotation = Numbers( lines[ i++ ], 4 );
			i++;
			var quaternion = new Quaternion( rotation[ 0 ], rotation[ 1 ], rotation[ 2 ], rotation[ 3 ] );
			quaternion = quaternion.LengthSquared() < 1e-12f ? Quaternion.Identity : Quaternion.Normalize( quaternion );
			motion.Add( name, new MmdKeyframe( 0, new Vector3( position[ 0 ], position[ 1 ], position[ 2 ] ), quaternion, Array.Empty<byte>() ) );
			readBones++;
		}
		if ( readBones != count )
			throw new FormatException( "VPD bone count does not match its pose blocks." );
		return motion;
	}

	private static float[] Numbers( string line, int count )
	{
		var parts = line.TrimEnd( ';' ).Split( ',' );
		if ( parts.Length != count )
			throw new FormatException( "Invalid VPD vector." );
		var values = new float[ count ];
		for ( var i = 0; i < count; i++ )
		{
			if ( !float.TryParse( parts[ i ], NumberStyles.Float, CultureInfo.InvariantCulture, out values[ i ] ) || !float.IsFinite( values[ i ] ) )
				throw new FormatException( "Invalid VPD number." );
		}
		return values;
	}
}
