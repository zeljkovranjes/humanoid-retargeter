using System.Numerics;
using System.Text;

namespace HumanoidRetargeter.Tests.Formats;

internal static class MmdFixture
{
	internal static readonly (string Name, int Parent, Vector3 Position)[] Bones =
	{
		("センター", -1, new( 0, 10, 0 )),
		("下半身", 0, new( 0, 10, 0 )),
		("上半身", 0, new( 0, 11, 0 )),
		("首", 2, new( 0, 16, 0 )),
		("頭", 3, new( 0, 17, 0 )),
		("左肩", 2, new( 1, 15, 0 )),
		("左腕", 5, new( 2, 15, 0 )),
		("左ひじ", 6, new( 5, 14, 0 )),
		("左手首", 7, new( 8, 13, 0 )),
		("右肩", 2, new( -1, 15, 0 )),
		("右腕", 9, new( -2, 15, 0 )),
		("右ひじ", 10, new( -5, 14, 0 )),
		("右手首", 11, new( -8, 13, 0 )),
		("左足", 1, new( 1, 10, 0 )),
		("左ひざ", 13, new( 1, 5, -0.1f )),
		("左足首", 14, new( 1, 1, 0 )),
		("左つま先", 15, new( 1, 0, -1 )),
		("右足", 1, new( -1, 10, 0 )),
		("右ひざ", 17, new( -1, 5, -0.1f )),
		("右足首", 18, new( -1, 1, 0 )),
		("右つま先", 19, new( -1, 0, -1 )),
		("左足ＩＫ", -1, new( 1, 1, 0 )),
	};
	internal static Encoding Japanese => CodePagesEncodingProvider.Instance.GetEncoding( 932 )!;

	internal static byte[] Pmx( int indexSize = 1, bool utf16 = false, int weightType = 0, bool ik = false )
	{
		using var stream = new MemoryStream();
		using var w = new BinaryWriter( stream );
		var encoding = utf16 ? Encoding.Unicode : Encoding.UTF8;
		void Text( string text )
		{
			var bytes = encoding.GetBytes( text );
			w.Write( bytes.Length );
			w.Write( bytes );
		}
		void Index( int value )
		{
			if ( indexSize == 1 ) w.Write( (sbyte)value );
			else if ( indexSize == 2 ) w.Write( (short)value );
			else w.Write( value );
		}
		w.Write( Encoding.ASCII.GetBytes( "PMX " ) );
		w.Write( weightType == 4 ? 2.1f : 2f );
		w.Write( new byte[] { 8, (byte)(utf16 ? 0 : 1), 1, (byte)indexSize, (byte)indexSize, (byte)indexSize, (byte)indexSize, (byte)indexSize, (byte)indexSize } );
		Text( "Original test character" );
		Text( "Test" );
		Text( "" );
		Text( "" );
		w.Write( 3 );
		for ( var i = 0; i < 3; i++ )
		{
			Vector( w, new Vector3( i, 10, -1 ) );
			Vector( w, new Vector3( 0, 0, -1 ) );
			w.Write( i * 0.5f );
			w.Write( 0.25f );
			w.Write( new byte[ 16 ] ); // additional UV
			w.Write( (byte)weightType );
			var influences = weightType == 0 ? 1 : weightType is 1 or 3 ? 2 : 4;
			for ( var j = 0; j < influences; j++ ) Index( j );
			if ( influences == 2 ) w.Write( 0.25f );
			if ( influences == 4 ) for ( var j = 0; j < 4; j++ ) w.Write( 0.25f );
			if ( weightType == 3 ) w.Write( new byte[ 36 ] );
			w.Write( 1f );
		}
		w.Write( 3 );
		Index( 0 ); Index( 1 ); Index( 2 );
		w.Write( 1 );
		Text( "tex\\顔.bmp" );
		w.Write( 1 );
		Text( "skin" ); Text( "skin" );
		foreach ( var value in new[] { 0.5f, 0.75f, 1f, 0.8f } ) w.Write( value );
		w.Write( new byte[ 28 ] );
		w.Write( (byte)1 );
		w.Write( new byte[ 20 ] );
		Index( 0 ); Index( -1 );
		w.Write( (byte)0 ); w.Write( (byte)1 ); w.Write( (byte)0 );
		Text( "" );
		w.Write( 3 );
		w.Write( Bones.Length );
		for ( var i = 0; i < Bones.Length; i++ )
		{
			var bone = Bones[ i ];
			Text( bone.Name ); Text( "" );
			Vector( w, bone.Position );
			Index( bone.Parent );
			w.Write( 0 );
			w.Write( (ushort)(ik && i == 21 ? 0x20 : 0) );
			w.Write( new byte[ 12 ] );
			if ( ik && i == 21 )
			{
				Index( 15 );
				w.Write( 40 );
				w.Write( 0.5f );
				w.Write( 2 );
				Index( 14 ); w.Write( (byte)1 );
				Vector( w, new Vector3( -3.14f, 0, 0 ) );
				Vector( w, new Vector3( -0.002f, 0, 0 ) );
				Index( 13 ); w.Write( (byte)0 );
			}
		}
		w.Write( new byte[ 16 ] ); // morphs, display groups, rigid bodies, joints
		if ( weightType == 4 ) w.Write( 0 ); // PMX 2.1 soft bodies
		return stream.ToArray();
	}

	internal static byte[] Pmd()
	{
		using var stream = new MemoryStream();
		using var w = new BinaryWriter( stream );
		w.Write( Encoding.ASCII.GetBytes( "Pmd" ) );
		w.Write( 1f );
		w.Write( new byte[ 276 ] );
		w.Write( 3 );
		for ( var i = 0; i < 3; i++ )
		{
			Vector( w, new Vector3( i, 10, -1 ) );
			Vector( w, new Vector3( 0, 0, -1 ) );
			w.Write( 0f ); w.Write( 0f );
			w.Write( (ushort)0 ); w.Write( (ushort)1 );
			w.Write( (byte)75 ); w.Write( (byte)0 );
		}
		w.Write( 3 );
		w.Write( (ushort)0 ); w.Write( (ushort)1 ); w.Write( (ushort)2 );
		w.Write( 1 );
		for ( var i = 0; i < 4; i++ ) w.Write( 1f );
		w.Write( new byte[ 30 ] );
		w.Write( 3 );
		Fixed( w, "face.bmp*shine.sph", 20 );
		w.Write( (ushort)Bones.Length );
		foreach ( var bone in Bones )
		{
			Fixed( w, bone.Name, 20 );
			w.Write( (ushort)bone.Parent );
			w.Write( (ushort)0 ); w.Write( (byte)1 ); w.Write( (ushort)0 );
			Vector( w, bone.Position );
		}
		w.Write( (ushort)0 ); // IK
		w.Write( (ushort)0 ); // morphs
		w.Write( new byte[ 6 ] );
		return stream.ToArray();
	}

	internal static byte[] Vmd( string name = "センター", bool curved = false, bool old = false, bool disableIk = false )
	{
		using var stream = new MemoryStream();
		using var w = new BinaryWriter( stream );
		Fixed( w, old ? "Vocaloid Motion Data file" : "Vocaloid Motion Data 0002", 30 );
		Fixed( w, "test", old ? 10 : 20 );
		w.Write( 2 );
		foreach ( var frame in new[] { 30, 0 } ) // intentionally out of order
		{
			Fixed( w, name, 15 );
			w.Write( frame );
			Vector( w, frame == 0 ? Vector3.Zero : new Vector3( 2, 1, -1 ) );
			w.Write( 0f ); w.Write( 0f ); w.Write( 0f ); w.Write( 1f );
			var curve = new byte[ 64 ];
			for ( var c = 0; c < 4; c++ )
			{
				curve[ c ] = 20; curve[ c + 4 ] = 20;
				curve[ c + 8 ] = 107; curve[ c + 12 ] = 107;
			}
			if ( curved ) { curve[ 0 ] = 0; curve[ 4 ] = 0; curve[ 8 ] = 127; curve[ 12 ] = 0; }
			w.Write( curve );
		}
		w.Write( new byte[ 16 ] );
		w.Write( disableIk ? 1 : 0 );
		if ( disableIk )
		{
			w.Write( 0 ); w.Write( (byte)1 ); w.Write( 1 );
			Fixed( w, "左足ＩＫ", 20 ); w.Write( (byte)0 );
		}
		return stream.ToArray();
	}

	internal static byte[] Vpd( string name = "左手首" ) => Japanese.GetBytes(
		$"Vocaloid Pose Data file\r\n\r\nmodel.osm; // source\r\n1; // count\r\n\r\nBone0{{{name}\r\n1,2,3;\r\n0,0,0.70710678,0.70710678;\r\n}}\r\n" );

	internal static byte[] PhysicsModel( int mode, bool lockedJoint = false )
	{
		using var stream = new MemoryStream();
		using var w = new BinaryWriter( stream );
		w.Write( Pmx()[ ..^16 ] );
		w.Write( 0 ); // morphs
		w.Write( 0 ); // display groups
		w.Write( lockedJoint ? 2 : 1 );
		void Body( int bone, int bodyMode )
		{
			w.Write( 0 ); w.Write( 0 );
			w.Write( (sbyte)bone );
			w.Write( (byte)0 ); w.Write( (ushort)0 ); w.Write( (byte)0 );
			Vector( w, new Vector3( 0.1f ) );
			Vector( w, Bones[ bone ].Position ); Vector( w, Vector3.Zero );
			w.Write( 1f ); w.Write( 0f ); w.Write( 0f ); w.Write( 0f ); w.Write( 0.5f );
			w.Write( (byte)bodyMode );
		}
		Body( 6, mode );
		if ( lockedJoint ) Body( 5, 0 );
		w.Write( lockedJoint ? 1 : 0 );
		if ( lockedJoint )
		{
			w.Write( 0 ); w.Write( 0 ); w.Write( (byte)0 );
			w.Write( (sbyte)1 ); w.Write( (sbyte)0 );
			Vector( w, Bones[ 5 ].Position );
			for ( var i = 0; i < 7; i++ ) Vector( w, Vector3.Zero );
		}
		return stream.ToArray();
	}

	internal static byte[] MorphModel()
	{
		using var stream = new MemoryStream();
		using var w = new BinaryWriter( stream );
		w.Write( Pmx()[ ..^16 ] );
		w.Write( 1 );
		var name = Encoding.UTF8.GetBytes( "笑い" );
		w.Write( name.Length ); w.Write( name ); w.Write( 0 );
		w.Write( (byte)1 ); w.Write( (byte)1 ); w.Write( 1 );
		w.Write( (byte)0 ); Vector( w, new Vector3( 1, 2, 3 ) );
		w.Write( new byte[ 12 ] );
		return stream.ToArray();
	}

	internal static byte[] MorphMotion()
	{
		using var stream = new MemoryStream();
		using var w = new BinaryWriter( stream );
		Fixed( w, "Vocaloid Motion Data 0002", 30 ); Fixed( w, "test", 20 );
		w.Write( 0 ); w.Write( 2 );
		Fixed( w, "笑い", 15 ); w.Write( 0 ); w.Write( 0f );
		Fixed( w, "笑い", 15 ); w.Write( 30 ); w.Write( 1f );
		w.Write( new byte[ 16 ] );
		return stream.ToArray();
	}

	internal static byte[] AdvancedModel()
	{
		using var stream = new MemoryStream();
		using var w = new BinaryWriter( stream );
		var prefix = Pmx()[ ..^16 ];
		BitConverter.GetBytes( 2.1f ).CopyTo( prefix, 4 );
		w.Write( prefix );
		w.Write( 4 );
		void Morph( string name, byte kind )
		{
			var text = Encoding.UTF8.GetBytes( name );
			w.Write( text.Length ); w.Write( text ); w.Write( 0 );
			w.Write( (byte)1 ); w.Write( kind ); w.Write( 1 );
		}
		Morph( "uv", 3 );
		w.Write( (byte)0 );
		foreach ( var value in new[] { 0.2f, 0.3f, 0f, 0f } ) w.Write( value );
		Morph( "uv1", 4 );
		w.Write( (byte)1 );
		foreach ( var value in new[] { 1f, 2f, 3f, 4f } ) w.Write( value );
		Morph( "material", 8 );
		w.Write( (sbyte)-1 ); w.Write( (byte)1 );
		for ( var i = 0; i < 28; i++ ) w.Write( (i + 1) * 0.1f );
		Morph( "impulse", 10 );
		w.Write( (sbyte)0 ); w.Write( (byte)1 );
		Vector( w, new Vector3( 1, 2, 3 ) ); Vector( w, new Vector3( 4, 5, 6 ) );
		w.Write( PhysicsModel( 1 )[ (prefix.Length + 4).. ] );
		w.Write( 1 );
		w.Write( 0 ); w.Write( 0 );
		w.Write( (byte)0 ); w.Write( (sbyte)0 ); w.Write( (byte)0 ); w.Write( (ushort)0 ); w.Write( (byte)1 );
		w.Write( 2 ); w.Write( 0 ); w.Write( 1f ); w.Write( 0.05f ); w.Write( 0 );
		for ( var i = 0; i < 18; i++ ) w.Write( i == 11 ? 1f : 0f );
		for ( var i = 0; i < 4; i++ ) w.Write( 4 );
		for ( var i = 0; i < 3; i++ ) w.Write( 1f );
		w.Write( 1 ); w.Write( (sbyte)0 ); w.Write( (byte)1 ); w.Write( (byte)0 );
		w.Write( 1 ); w.Write( (byte)0 );
		return stream.ToArray();
	}

	private static void Fixed( BinaryWriter writer, string text, int length )
	{
		var bytes = Japanese.GetBytes( text );
		writer.Write( bytes.Take( length ).ToArray() );
		writer.Write( new byte[ Math.Max( 0, length - bytes.Length ) ] );
	}

	private static void Vector( BinaryWriter writer, Vector3 value )
	{
		writer.Write( value.X ); writer.Write( value.Y ); writer.Write( value.Z );
	}
}
