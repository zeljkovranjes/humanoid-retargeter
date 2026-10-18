using System.Numerics;

namespace HumanoidRetargeter.Core.Formats.Mmd;

using Vector3 = System.Numerics.Vector3;

internal static class MmdMorphReader
{
	internal static void ReadPmx( MmdReader reader, MmdModel model, int vertexSize, int boneSize, int materialSize, int morphSize, int rigidSize )
	{
		var count = reader.Count( 14, 4096 );
		for ( var i = 0; i < count; i++ )
		{
			var morph = new MmdMorph { Name = reader.Text() };
			reader.Text();
			reader.Byte();
			var kind = reader.Byte();
			var offsets = reader.Count();
			for ( var j = 0; j < offsets; j++ )
			{
				switch ( kind )
				{
					case 0:
					case 9:
						morph.Groups.Add( (reader.Index( morphSize ), reader.Float()) );
						break;
					case 1:
						var vertex = reader.VertexIndex( vertexSize );
						if ( vertex < 0 || vertex >= model.Vertices.Count )
							throw new FormatException( "Invalid PMX morph vertex." );
						morph.Vertices[ vertex ] = reader.Vector();
						break;
					case 2:
						var bone = reader.Index( boneSize );
						if ( bone < 0 || bone >= model.Bones.Count )
							throw new FormatException( "Invalid PMX morph bone." );
						morph.Bones.Add( (bone, reader.Vector(), reader.Rotation()) );
						break;
					case >= 3 and <= 7:
						var uvVertex = reader.VertexIndex( vertexSize );
						if ( uvVertex < 0 || uvVertex >= model.Vertices.Count )
							throw new FormatException( "Invalid PMX UV morph vertex." );
						morph.Uvs.Add( (uvVertex, kind - 3, reader.Vector4()) );
						break;
					case 8:
						var material = reader.Index( materialSize );
						var operation = reader.Byte();
						if ( material < -1 || material >= model.Materials.Count || operation > 1 )
							throw new FormatException( "Invalid PMX material morph." );
						morph.Materials.Add( new MmdMaterialMorph
						{
							Material = material, Add = operation == 1,
							Diffuse = reader.Vector4(), Specular = reader.Vector(), Power = reader.Float(),
							Ambient = reader.Vector(), EdgeColor = reader.Vector4(), EdgeSize = reader.Float(),
							Texture = reader.Vector4(), Sphere = reader.Vector4(), Toon = reader.Vector4(),
						} );
						break;
					case 10:
						var body = reader.Index( rigidSize );
						var local = reader.Byte();
						if ( body < 0 || local > 1 )
							throw new FormatException( "Invalid PMX impulse morph." );
						morph.Impulses.Add( (body, local == 1, reader.Vector(), reader.Vector()) );
						break;
					default:
						throw new FormatException( "Unknown PMX morph type." );
				}
			}
			model.Morphs.Add( morph );
		}
		ValidateGroups( model );
	}

	internal static void ReadPmd( MmdReader reader, MmdModel model )
	{
		var count = reader.U16();
		if ( count > 4096 )
			throw new FormatException( "Invalid PMD morph count." );
		int[] baseVertices = Array.Empty<int>();
		for ( var i = 0; i < count; i++ )
		{
			var morph = new MmdMorph { Name = reader.JapaneseText( 20 ) };
			var offsets = reader.Count( 16 );
			var kind = reader.Byte();
			if ( kind == 0 )
				baseVertices = new int[ offsets ];
			for ( var j = 0; j < offsets; j++ )
			{
				var index = reader.Int();
				var position = reader.Vector();
				if ( kind == 0 )
				{
					if ( index < 0 || index >= model.Vertices.Count )
						throw new FormatException( "Invalid PMD base morph vertex." );
					baseVertices[ j ] = index;
				}
				else
				{
					if ( index < 0 || index >= baseVertices.Length )
						throw new FormatException( "Invalid PMD morph base index." );
					morph.Vertices[ baseVertices[ index ] ] = position;
				}
			}
			if ( kind != 0 )
				model.Morphs.Add( morph );
		}
	}

	private static void ValidateGroups( MmdModel model )
	{
		var state = new byte[ model.Morphs.Count ];
		void Visit( int index )
		{
			if ( index < 0 || index >= model.Morphs.Count || state[ index ] == 1 )
				throw new FormatException( "Invalid or cyclic PMX group morph." );
			if ( state[ index ] == 2 )
				return;
			state[ index ] = 1;
			foreach ( var child in model.Morphs[ index ].Groups )
				Visit( child.Morph );
			state[ index ] = 2;
		}
		for ( var i = 0; i < state.Length; i++ )
			Visit( i );
	}
}
