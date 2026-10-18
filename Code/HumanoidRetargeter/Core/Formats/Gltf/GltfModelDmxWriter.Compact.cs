using System.Linq;

namespace HumanoidRetargeter.Core.Formats.Gltf;

public static partial class GltfModelDmxWriter
{
	private static MeshPart Compact( MeshPart part )
	{
		// MMD materials share one vertex buffer. Export only the vertices each uses.
		var indices = part.Triangles.Distinct().OrderBy( index => index ).ToArray();
		if ( indices.Length == part.Positions.Length )
			return part;
		var remap = new int[ part.Positions.Length ];
		for ( var i = 0; i < indices.Length; i++ )
			remap[ indices[ i ] ] = i;
		part.Positions = indices.Select( index => part.Positions[ index ] ).ToArray();
		part.Normals = indices.Select( index => part.Normals[ index ] ).ToArray();
		part.TexCoords = indices.Select( index => part.TexCoords[ index ] ).ToArray();
		part.Weights = indices.SelectMany( index => part.Weights.Skip( index * 4 ).Take( 4 ) ).ToArray();
		part.Joints = indices.SelectMany( index => part.Joints.Skip( index * 4 ).Take( 4 ) ).ToArray();
		part.Triangles = part.Triangles.Select( index => remap[ index ] ).ToArray();
		foreach ( var name in part.Morphs.Keys.ToArray() )
			part.Morphs[ name ] = indices.Select( index => part.Morphs[ name ][ index ] ).ToArray();
		return part;
	}
}
