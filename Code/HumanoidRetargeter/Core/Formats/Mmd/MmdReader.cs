using System.Numerics;
using System.Text;

namespace HumanoidRetargeter.Core.Formats.Mmd;

using Vector3 = System.Numerics.Vector3;

// Bounds checks live here so corrupt counts cannot drive allocations or seeks outside the file.
internal sealed class MmdReader
{
	private readonly byte[] _data;
	private int _offset;

	internal MmdReader( byte[] data ) => _data = data;
	internal int Remaining => _data.Length - _offset;
	internal Encoding TextEncoding { get; set; } = Encoding.UTF8;

	internal void Skip( int count )
	{
		if ( count < 0 || count > Remaining )
			throw new FormatException( "Truncated MMD file or invalid section length." );
		_offset += count;
	}

	internal byte Byte()
	{
		Skip( 1 );
		return _data[ _offset - 1 ];
	}

	internal int U16() => Byte() | (Byte() << 8);
	internal int Int() => U16() | (U16() << 16);
	internal int Index( int size ) => size switch
	{
		1 => (sbyte)Byte(),
		2 => (short)U16(),
		4 => Int(),
		_ => throw new FormatException( "MMD index size must be 1, 2 or 4." ),
	};
	internal int VertexIndex( int size ) => size == 1 ? Byte() : size == 2 ? U16() : Int();

	internal int Count( int minimumBytes = 1, int maximum = 4_000_000 )
	{
		var count = Int();
		if ( count < 0 || count > maximum || count > Remaining / minimumBytes )
			throw new FormatException( "Invalid MMD element count." );
		return count;
	}

	internal float Float()
	{
		var value = BitConverter.Int32BitsToSingle( Int() );
		if ( !float.IsFinite( value ) )
			throw new FormatException( "MMD contains a non-finite number." );
		return value;
	}

	internal Vector3 Vector() => new( Float(), Float(), Float() );
	internal System.Numerics.Vector4 Vector4() => new( Float(), Float(), Float(), Float() );
	internal Quaternion Rotation()
	{
		var value = new Quaternion( Float(), Float(), Float(), Float() );
		return value.LengthSquared() < 1e-12f ? Quaternion.Identity : Quaternion.Normalize( value );
	}

	internal byte[] Bytes( int count )
	{
		Skip( count );
		return _data.AsSpan( _offset - count, count ).ToArray();
	}

	internal string FixedText( int count, Encoding encoding )
	{
		var bytes = Bytes( count );
		var end = Array.IndexOf( bytes, (byte)0 );
		return encoding.GetString( bytes, 0, end < 0 ? bytes.Length : end );
	}

	internal string Text() => TextEncoding.GetString( Bytes( Count() ) );

	internal string JapaneseText( int count )
	{
		var bytes = Bytes( count );
		var end = Array.IndexOf( bytes, (byte)0 );
		return MmdJapaneseEncoding.Decode( bytes, end < 0 ? bytes.Length : end );
	}
}
