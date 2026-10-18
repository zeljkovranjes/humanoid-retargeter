using System.Numerics;
using HumanoidRetargeter.Core.Maths;

namespace HumanoidRetargeter.Core.Formats.Mmd;

using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

internal sealed class MmdPlaybackState
{
	internal readonly MmdModel Model;
	internal readonly MmdPlaybackData Data;
	internal readonly XForm[] Palette;
	internal readonly Vector3[] Positions;
	internal readonly Vector3[] Normals;
	internal readonly Vector4[][] Uvs;
	internal readonly MmdMaterial[] Materials;
	private readonly MmdMorphWeights _weights;

	internal MmdPlaybackState( MmdPlaybackData data, MmdModel model = null )
	{
		Data = data;
		Model = model ?? MmdModelReader.Read( data.Model );
		Palette = new XForm[ Model.Bones.Count ];
		Positions = new Vector3[ Model.Vertices.Count ];
		Normals = new Vector3[ Positions.Length ];
		Uvs = Enumerable.Range( 0, 5 ).Select( _ => new Vector4[ Positions.Length ] ).ToArray();
		Materials = Model.Materials.Select( material => material.Copy() ).ToArray();
		_weights = new MmdMorphWeights( Model );
	}

	internal void Evaluate( float frame )
	{
		frame = Math.Clamp( frame, 0, Data.FrameCount - 1 );
		var low = (int)frame;
		var high = Math.Min( low + 1, Data.FrameCount - 1 );
		var blend = frame - low;
		for ( var bone = 0; bone < Palette.Length; bone++ )
		{
			var a = Data.TransformAt( low, bone, Palette.Length );
			var b = Data.TransformAt( high, bone, Palette.Length );
			Palette[ bone ] = new XForm( Vector3.Lerp( a.Pos, b.Pos, blend ), Quaternion.Slerp( a.Rot, b.Rot, blend ) );
		}
		for ( var i = 0; i < Positions.Length; i++ )
		{
			var vertex = Model.Vertices[ i ];
			Positions[ i ] = vertex.Position;
			Uvs[ 0 ][ i ] = new Vector4( vertex.Uv, 0, 0 );
			for ( var channel = 1; channel < 5; channel++ )
				Uvs[ channel ][ i ] = channel <= vertex.AdditionalUv.Length ? vertex.AdditionalUv[ channel - 1 ] : Vector4.Zero;
		}
		for ( var i = 0; i < Materials.Length; i++ )
			Materials[ i ].Reset( Model.Materials[ i ] );
		_weights.Sample( Data.Morphs, frame );
		for ( var i = 0; i < _weights.Values.Length; i++ )
		{
			var weight = _weights.Values[ i ];
			if ( weight == 0 )
				continue;
			var morph = Model.Morphs[ i ];
			foreach ( var (vertex, delta) in morph.Vertices )
				Positions[ vertex ] += delta * weight;
			foreach ( var uv in morph.Uvs )
				Uvs[ uv.Channel ][ uv.Vertex ] += uv.Offset * weight;
		}
		// PMX accumulates multiplicative factors and additive offsets separately.
		for ( var operation = 0; operation < 2; operation++ )
			for ( var i = 0; i < _weights.Values.Length; i++ )
				foreach ( var material in Model.Morphs[ i ].Materials )
				{
					if ( material.Add != (operation == 1) || _weights.Values[ i ] == 0 )
						continue;
					if ( material.Material >= 0 )
						material.Apply( Materials[ material.Material ], _weights.Values[ i ] );
					else
						foreach ( var target in Materials )
							material.Apply( target, _weights.Values[ i ] );
				}
		for ( var i = 0; i < Positions.Length; i++ )
			MmdSkinning.Deform( Model.Vertices[ i ], Positions[ i ], Palette, out Positions[ i ], out Normals[ i ] );
		for ( var i = 0; i < Data.SoftVertices.Length; i++ )
		{
			Vector3 At( int sample )
			{
				var offset = (sample * Data.SoftVertices.Length + i) * 3;
				return new Vector3( Data.SoftPositions[ offset ], Data.SoftPositions[ offset + 1 ], Data.SoftPositions[ offset + 2 ] );
			}
			Positions[ Data.SoftVertices[ i ] ] = Vector3.Lerp( At( low ), At( high ), blend );
		}
		if ( Data.SoftVertices.Length > 0 )
			UpdateNormals();
	}

	private void UpdateNormals()
	{
		Array.Clear( Normals, 0, Normals.Length );
		for ( var i = 0; i < Model.Indices.Length; i += 3 )
		{
			var a = Model.Indices[ i ];
			var b = Model.Indices[ i + 1 ];
			var c = Model.Indices[ i + 2 ];
			var normal = Vector3.Cross( Positions[ b ] - Positions[ a ], Positions[ c ] - Positions[ a ] );
			Normals[ a ] += normal;
			Normals[ b ] += normal;
			Normals[ c ] += normal;
		}
		for ( var i = 0; i < Normals.Length; i++ )
			Normals[ i ] = Normals[ i ].LengthSquared() > 1e-12f ? Vector3.Normalize( Normals[ i ] ) : Vector3.UnitY;
	}
}
