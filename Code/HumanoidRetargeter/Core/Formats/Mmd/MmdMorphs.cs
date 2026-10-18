using System.Numerics;
using HumanoidRetargeter.Core.Maths;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Core.Formats.Mmd;

using Vector3 = System.Numerics.Vector3;

internal static class MmdMorphs
{
	internal static Dictionary<int, Vector3> VertexOffsets( MmdModel model, int morph )
	{
		var offsets = new Dictionary<int, Vector3>();
		void Accumulate( int index, float weight )
		{
			foreach ( var (vertex, delta) in model.Morphs[ index ].Vertices )
				offsets[ vertex ] = offsets.GetValueOrDefault( vertex ) + delta * weight;
			foreach ( var child in model.Morphs[ index ].Groups )
				Accumulate( child.Morph, weight * child.Weight );
		}
		Accumulate( morph, 1f );
		return offsets;
	}

	internal static float Sample( List<(int Frame, float Weight)> keys, float frame )
	{
		if ( frame <= keys[ 0 ].Frame )
			return keys[ 0 ].Weight;
		for ( var i = 1; i < keys.Count; i++ )
		{
			if ( keys[ i ].Frame >= frame )
			{
				var a = keys[ i - 1 ];
				var b = keys[ i ];
				return a.Weight + (b.Weight - a.Weight) * (frame - a.Frame) / (b.Frame - a.Frame);
			}
		}
		return keys[ ^1 ].Weight;
	}

	internal static void ApplyBones( MmdModel model, SkeletonModel skeleton, XForm[] pose, Dictionary<string, float> weights )
	{
		void Apply( int index, float weight )
		{
			foreach ( var offset in model.Morphs[ index ].Bones )
			{
				var bone = skeleton.IndexOf( model.Bones[ offset.Bone ].Name );
				pose[ bone ].Pos += MmdModel.Position( offset.Position ) * weight;
				pose[ bone ].Rot = Quaternion.Normalize( pose[ bone ].Rot * Quaternion.Slerp( Quaternion.Identity, MmdModel.Rotation( offset.Rotation ), weight ) );
			}
			foreach ( var child in model.Morphs[ index ].Groups )
				Apply( child.Morph, weight * child.Weight );
		}
		for ( var i = 0; i < model.Morphs.Count; i++ )
		{
			if ( weights.TryGetValue( model.Morphs[ i ].Name, out var weight ) && weight != 0 )
				Apply( i, weight );
		}
	}
}
