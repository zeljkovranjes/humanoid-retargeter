using System.Numerics;
using HumanoidRetargeter.Core.Maths;

namespace HumanoidRetargeter.Core.Formats.Mmd;

using Vector3 = System.Numerics.Vector3;

internal static class MmdSkinning
{
	// SDEF adapted from babylon-mmd's sdefVertex.ts (MIT, Copyright 2024 noname).
	// See THIRD_PARTY_NOTICES.md. Inputs and outputs stay in original MMD coordinates.
	internal static void Deform( MmdVertex vertex, Vector3 position, XForm[] palette, out Vector3 result, out Vector3 normal )
	{
		if ( vertex.Deform == 3 && vertex.Weights[ 0 ] > 0 && vertex.Weights[ 1 ] > 0 )
		{
			var a = palette[ vertex.Bones[ 0 ] ];
			var b = palette[ vertex.Bones[ 1 ] ];
			var weight = vertex.Weights[ 0 ];
			var rotation = Quaternion.Slerp( a.Rot, b.Rot, 1 - weight );
			var difference = (vertex.SdefR0 - vertex.SdefR1) * 0.5f;
			var center0 = vertex.SdefC + difference * (1 - weight);
			var center1 = vertex.SdefC - difference * weight;
			result = Vector3.Transform( position - vertex.SdefC, rotation )
				+ Point( a, center0 ) * weight + Point( b, center1 ) * (1 - weight);
			normal = Vector3.Transform( vertex.Normal, rotation );
			return;
		}
		if ( vertex.Deform == 4 )
		{
			// Hemisphere-correct, normalized dual quaternion blend preserves rigid rotation.
			var real = new Quaternion();
			var dual = new Quaternion();
			var reference = Quaternion.Identity;
			for ( var i = 0; i < 4; i++ )
				if ( vertex.Weights[ i ] > 0 )
				{
					reference = palette[ vertex.Bones[ i ] ].Rot;
					break;
				}
			for ( var i = 0; i < 4; i++ )
			{
				if ( vertex.Weights[ i ] == 0 )
					continue;
				var transform = palette[ vertex.Bones[ i ] ];
				var weight = vertex.Weights[ i ] * (Quaternion.Dot( reference, transform.Rot ) < 0 ? -1 : 1);
				real += transform.Rot * weight;
				dual += new Quaternion( transform.Pos, 0 ) * transform.Rot * (0.5f * weight);
			}
			var length = real.Length();
			if ( length > 1e-8f )
			{
				real *= 1 / length;
				dual *= 1 / length;
				dual -= real * Quaternion.Dot( real, dual );
				var translation = dual * Quaternion.Conjugate( real ) * 2;
				result = Vector3.Transform( position, real ) + new Vector3( translation.X, translation.Y, translation.Z );
				normal = Vector3.Transform( vertex.Normal, real );
				return;
			}
		}
		result = Vector3.Zero;
		normal = Vector3.Zero;
		for ( var i = 0; i < 4; i++ )
		{
			if ( vertex.Weights[ i ] == 0 )
				continue;
			var transform = palette[ vertex.Bones[ i ] ];
			result += Point( transform, position ) * vertex.Weights[ i ];
			normal += Vector3.Transform( vertex.Normal, transform.Rot ) * vertex.Weights[ i ];
		}
		if ( normal.LengthSquared() > 1e-12f )
			normal = Vector3.Normalize( normal );
	}

	internal static Vector3 Point( XForm transform, Vector3 point ) => Vector3.Transform( point, transform.Rot ) + transform.Pos;
}
